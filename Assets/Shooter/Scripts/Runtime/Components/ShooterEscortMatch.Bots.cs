using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using Blocks.Gameplay.Core;

namespace Blocks.Gameplay.Shooter
{
    public struct ShooterBotState : INetworkSerializable, IEquatable<ShooterBotState>
    {
        public Vector3 position;
        public float yaw, health;
        public double respawnAt;
        public bool pilot;
        public int deaths, life, character;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        { s.SerializeValue(ref position); s.SerializeValue(ref yaw); s.SerializeValue(ref health); s.SerializeValue(ref respawnAt); s.SerializeValue(ref deaths); s.SerializeValue(ref life); s.SerializeValue(ref character); s.SerializeValue(ref pilot); }
        public bool Equals(ShooterBotState b) => position==b.position && yaw==b.yaw && health==b.health && respawnAt==b.respawnAt && deaths==b.deaths && life==b.life && character==b.character && pilot==b.pilot;
    }

    public sealed partial class ShooterEscortMatch
    {
        NetworkList<ShooterBotState> botStates;
        static readonly string[] BotDifficultyNames = { "브론즈", "실버", "골드", "다이아", "마스터" };
        // All clients display the authority's selection; only the authority simulates combat.
        readonly NetworkVariable<int> botDifficulty = new NetworkVariable<int>(2);
        int BotDifficulty => Mathf.Clamp(botDifficulty.Value, 0, 4);
        public string BotDifficultyName => BotDifficultyNames[BotDifficulty];
        static readonly float[] BotReaction = { .95f, .65f, .4f, .22f, .10f };
        static readonly float[] BotThinkInterval = { .30f, .25f, .18f, .10f, .05f };
        static readonly float[] BotAimDegrees = { 6f, 4f, 2.2f, 1f, .35f };
        static readonly float[] BotFireDelay = { 1.8f, 1.4f, 1f, .85f, .7f };
        readonly ulong[] botTrackedTarget = new ulong[4];
        readonly float[] botReactAt = new float[4];
        void CycleBotDifficulty()
        {
            if(!HasAuthority || phase.Value!=0 || sessionBusy)return;
            botDifficulty.Value=(BotDifficulty+1)%BotDifficultyNames.Length;
        }
        readonly NetworkVariable<bool> fillWithBots = new NetworkVariable<bool>(false);
        readonly ShooterTrainingBot[] botViews = new ShooterTrainingBot[4];
        public IEnumerable<ShooterTrainingBot> TargetBots => botViews.Where(view=>view!=null && view.gameObject.activeInHierarchy);
        public bool TryGetTargetBot(ShooterTrainingBot view,out ShooterBotState state)
        {
            state=default;
            int i=Array.IndexOf(botViews,view);
            if(i<0 || botStates==null || i>=botStates.Count || !BotInSlot(i))return false;
            state=botStates[i];return true;
        }
        readonly float[] botThinkAt = new float[4], botFireAt = new float[4];
        readonly int[] botAmmo = new int[4];
        public static string BotHeroName(int hero) => hero==4 ? "디바 파일럿" : new[] { "솔저", "겐지", "디바", "캐서디" }[Mathf.Clamp(hero,0,3)];
        public static float BotMaxHealth(int hero) => 100f;
        static int BotMagazine(int hero) => hero==4 ? 7 : hero==1 ? 24 : hero==3 ? 6 : hero==2 ? 0 : 30;
        string BotDisplayName(int slot) => "AI "+BotHeroName(botStates[slot].character)+" "+(slot+1);
        NavMeshData botNavData;
        NavMeshDataInstance botNavInstance;
        AsyncOperation botNavBuild;
        float botSyncAt;
        int botRound=-1;
        public static ulong BotId(int slot) => ulong.MaxValue-1-(ulong)slot;
        public static bool IsBotId(ulong id) => id>=ulong.MaxValue-4 && id<ulong.MaxValue;
        bool BotInSlot(int i) => i>=0 && i<slots.Count && slots[i]==BotId(i);

        void InitializeBots() { botStates=new NetworkList<ShooterBotState>(); }
        void ResetBots()
        {
            CleanupBots();
            if(HasAuthority) { botStates.Clear();for(int i=0;i<4;i++)botStates.Add(default); fillWithBots.Value=false; }
        }
        void CleanupBots()
        {
            foreach(var view in botViews)if(view!=null)Destroy(view.gameObject);
            Array.Clear(botViews,0,botViews.Length);
            if(botNavData!=null) { NavMeshBuilder.Cancel(botNavData); if(botNavInstance.valid)botNavInstance.Remove(); Destroy(botNavData); }
            botNavData=null;botNavBuild=null;botRound=-1;
        }
        void FillBotSlots()
        {
            for(int i=0;i<slots.Count;i++)
            {
                if(!fillWithBots.Value && BotInSlot(i)) {readyPlayers.Remove(slots[i]);slots[i]=Empty;}
                if(fillWithBots.Value && slots[i]==Empty)slots[i]=BotId(i);
                if(BotInSlot(i))
                {
                    var state=botStates[i];state.character=i%4;botStates[i]=state;
                    if(!readyPlayers.Contains(slots[i]))readyPlayers.Add(slots[i]);scoreNames[i]=new FixedString64Bytes(BotDisplayName(i));
                }
            }
        }
        void ToggleBots()
        {
            if(!HasAuthority || phase.Value!=0 || sessionBusy)return;
            fillWithBots.Value=!fillWithBots.Value;FillBotSlots();
        }
        void BuildBotNavigation()
        {
            if(botNavData!=null || !Enumerable.Range(0,slots.Count).Any(BotInSlot))return;
            var marks=new List<NavMeshBuildMarkup>();
            foreach(var player in WeaponController.ActivePlayers)
                if(player!=null)marks.Add(new NavMeshBuildMarkup { root=player.transform,ignoreFromBuild=true });
            foreach(var bot in botViews)if(bot!=null)marks.Add(new NavMeshBuildMarkup { root=bot.transform,ignoreFromBuild=true });
            if(train!=null)marks.Add(new NavMeshBuildMarkup { root=train,ignoreFromBuild=true });
            var bounds=new Bounds(Vector3.zero,new Vector3(300,100,300));
            var sources=new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(bounds,~0,NavMeshCollectGeometry.PhysicsColliders,0,marks,sources);
            var settings=NavMesh.GetSettingsByID(0);
            settings.agentRadius=.35f;settings.agentHeight=1.8f;settings.agentClimb=.4f;
            botNavData=new NavMeshData(settings.agentTypeID);
            botNavInstance=NavMesh.AddNavMeshData(botNavData);
            botNavBuild=NavMeshBuilder.UpdateNavMeshDataAsync(botNavData,settings,sources,bounds);
        }
        Vector3 BotSpawn(int i)
        {
            var points=i%2==0?attackSpawns:defendSpawns;
            Vector3 position=TeamMode && points!=null && points.Length>0 ? points[(i/2)%points.Length].position :
                (FindAnyObjectByType<GameManager>()?.GetTrainingSpawnPosition(i) ?? Vector3.zero);
            if(NavMesh.SamplePosition(position,out var hit,8,NavMesh.AllAreas))return hit.position;
            return position;
        }
        bool BotProtected(int i) => TeamMode && botStates[i].health>0 && spawnSafeAreas[i%2].Contains(botStates[i].position);
        void UpdateBots()
        {
            if(!IsSpawned || botStates.Count!=4)return;
            bool visible=phase.Value==1 || phase.Value==2 || phase.Value==4;
            for(int i=0;i<4;i++)
            {
                bool active=BotInSlot(i) && visible;
                if(active && botViews[i]==null)botViews[i]=ShooterTrainingBot.Create(this,i);
                if(botViews[i]==null)continue;
                botViews[i].gameObject.SetActive(active && botStates[i].health>0);
                if(active)botViews[i].RenderState(botStates[i],HasAuthority,TeamMode?i%2:-1);
            }
            if(!HasAuthority || !visible)return;
            BuildBotNavigation();
            if(botNavBuild!=null && !botNavBuild.isDone)return;
            if(botRound!=epoch.Value)
            {
                botRound=epoch.Value;
                for(int i=0;i<4;i++)if(BotInSlot(i))RespawnBot(i,true);
            }
            if(!CombatAllowed)return;
            bool sync=Time.time>=botSyncAt;if(sync)botSyncAt=Time.time+.1f;
            for(int i=0;i<4;i++)
            {
                if(!BotInSlot(i))continue;
                var state=botStates[i];var view=botViews[i];
                if(state.health<=0) {if(Now>=state.respawnAt)RespawnBot(i,false);continue;}
                var agent=view.Agent;
                if(!agent.enabled) {agent.enabled=true;if(!agent.isOnNavMesh){agent.enabled=false;continue;}}
                if(!agent.isOnNavMesh)continue;
                if(Time.time>=botThinkAt[i]) {botThinkAt[i]=Time.time+BotThinkInterval[BotDifficulty];ThinkBot(i);}
                if(sync) {state=botStates[i];state.position=view.transform.position;state.yaw=view.transform.eulerAngles.y;botStates[i]=state;}
            }
        }
        void RespawnBot(int i,bool reset)
        {
            var state=botStates[i];state.position=BotSpawn(i);state.health=BotMaxHealth(state.character);state.respawnAt=0;state.pilot=false;
            botAmmo[i]=BotMagazine(state.character);
            botTrackedTarget[i]=Empty;botReactAt[i]=0;
            state.life++;if(reset)state.deaths=0;botStates[i]=state;
            var view=botViews[i];view.gameObject.SetActive(true);view.transform.position=state.position;
            view.Agent.enabled=true;if(view.Agent.isOnNavMesh)view.Agent.Warp(state.position);else view.Agent.enabled=false;
            botFireAt[i]=Time.time+1;botThinkAt[i]=0;
        }
        void ThinkBot(int i)
        {
            var view=botViews[i];var position=view.transform.position;
            Vector3 target=Vector3.zero;float nearest=32;bool found=false,clear=false;
            ulong targetId=Empty;
            foreach(var player in WeaponController.ActivePlayers)
            {
                if(player==null || !player.IsSpawned || TeamOf(player.OwnerClientId)<0 ||
                   (TeamMode && TeamOf(player.OwnerClientId)==i%2) || IsSpawnProtected(player.OwnerClientId) ||
                   !player.GetComponent<CoreStatsHandler>().IsAlive || !player.GetComponent<CorePlayerState>().IsActive)continue;
                ConsiderBotTarget(view,player.transform.position,player.OwnerClientId,ref target,ref targetId,ref nearest,ref found,ref clear);
            }
            for(int j=0;j<4;j++)if(j!=i && BotInSlot(j) && botStates[j].health>0 && !BotProtected(j) && (!TeamMode || i%2!=j%2))
                ConsiderBotTarget(view,botStates[j].position,BotId(j),ref target,ref targetId,ref nearest,ref found,ref clear);
            bool canSee = found && clear && !BotProtected(i);
            if(!canSee)botTrackedTarget[i]=Empty;
            else if(botTrackedTarget[i]!=targetId)
            {
                botTrackedTarget[i]=targetId;
                botReactAt[i]=Time.time+BotReaction[BotDifficulty];
            }
            Vector3 goal=TeamMode?BotPayloadGoal(i):
                found?target:BotSpawn((i+1+(int)(Time.time/8))%4);
            if(found && clear && !BotProtected(i))
            {
                Vector3 direction=target-position;direction.y=0;
                if(direction.sqrMagnitude>.01f)view.transform.rotation=Quaternion.LookRotation(direction);
                int hero=botStates[i].pilot?4:botStates[i].character;
                float attackRange=hero==2?12:hero==3?24:32;
                if(Time.time>=botFireAt[i] && Time.time>=botReactAt[i] && nearest<attackRange)
                {
                    int magazine=BotMagazine(hero);
                    if(magazine>0 && botAmmo[i]<=0)
                    {botAmmo[i]=magazine;botFireAt[i]=Time.time+(hero==4?1.4f:hero==3?1.8f:1.5f);BotReloadRpc(i);}
                    else
                    {
                        botFireAt[i]=Time.time+Mathf.Max(hero==4?WeaponController.DvaPilotShotInterval:hero==1?.75f:hero==3?.5f:.12f, (hero==4?WeaponController.DvaPilotShotInterval:hero==1?.85f:hero==3?.65f:.25f)*BotFireDelay[BotDifficulty]);
                        if(hero==1)StartCoroutine(BotShurikenBurst(i,target,targetId,botStates[i].life,epoch.Value));
                        else {if(magazine>0)botAmmo[i]--;FireBot(i,target,targetId);}
                    }
                }
                if(!TeamMode && nearest<(hero==2?7:hero==1?14:18))goal=position;
            }
            if(NavMesh.SamplePosition(goal,out var hit,5,NavMesh.AllAreas))view.Agent.SetDestination(hit.position);
        }
        Vector3 BotPayloadGoal(int i)
        {
            Vector3 center=RoutePoint(distance.Value);
            // The initial cart borders the blue safe zone. Standing behind it would
            // leave an attacker protected forever, unable to shoot or push.
            Vector3[] offsets={new Vector3(1.6f,0,i%2==0?1.1f:-1.1f),new Vector3(-1.6f,0,i%2==0?1.1f:-1.1f),
                new Vector3(0,0,2.5f),new Vector3(0,0,-2.5f)};
            foreach(var offset in offsets)
                if(NavMesh.SamplePosition(center+offset,out var hit,1,NavMesh.AllAreas) && !spawnSafeAreas[i%2].Contains(hit.position))return hit.position;
            return center;
        }
        static bool TryBotCombatant(Collider collider,out ulong id)
        {
            var bot=collider.GetComponentInParent<ShooterTrainingBot>();
            if(bot!=null){id=bot.ParticipantId;return true;}
            var player=collider.GetComponentInParent<WeaponController>();
            if(player!=null && player.IsSpawned){id=player.OwnerClientId;return true;}
            id=Empty;return false;
        }
        bool BotCanAttack(ulong attacker,ulong target)
        {
            return attacker!=target && TeamOf(attacker)>=0 && TeamOf(target)>=0 &&
                (!TeamMode || TeamOf(attacker)!=TeamOf(target)) && !IsSpawnProtected(target);
        }
        void ConsiderBotTarget(ShooterTrainingBot view,Vector3 candidate,ulong candidateId,ref Vector3 target,ref ulong targetId,ref float nearest,ref bool found,ref bool clear)
        {
            if(!BotCanAttack(view.ParticipantId,candidateId))return;
            float d=Vector3.Distance(view.transform.position,candidate);if(d>=nearest)return;
            Vector3 origin=view.transform.position+Vector3.up*1.4f, aim=candidate+Vector3.up*1.1f;
            bool visible=!NinjaShurikenFlight.FirstHit(origin,(aim-origin).normalized,Vector3.Distance(origin,aim),view.transform,out var hit) ||
                (TryBotCombatant(hit.collider,out var hitId) && hitId==candidateId);
            // Keep pursuing occluded opponents, but never shoot through geometry.
            target=candidate;targetId=candidateId;nearest=d;found=true;clear=visible;
        }
        void FireBot(int i,Vector3 target,ulong targetId)
        {
            if(!CombatAllowed || !BotInSlot(i) || !BotCanAttack(BotId(i),targetId))return;
            var view=botViews[i];Vector3 origin=view.Muzzle.position;
            // Do not let a muzzle protruding past a teammate or wall bypass it.
            Vector3 eye=view.transform.position+Vector3.up*1.4f;
            Vector3 muzzleOffset=origin-eye;
            if(NinjaShurikenFlight.FirstHit(eye,muzzleOffset.normalized,muzzleOffset.magnitude,view.transform,out _))return;
            int hero=botStates[i].pilot?4:botStates[i].character;
            float damage=hero==4?20:hero==3?35:hero==1?18:hero==2?12:8;
            // Angular error scales with range; high ranks still obey walls and weapon spread.
            float error = Mathf.Tan(BotAimDegrees[BotDifficulty]*Mathf.Deg2Rad)*Vector3.Distance(origin,target);
            Vector3 aim=target+Vector3.up*1.15f+UnityEngine.Random.insideUnitSphere*(error+(hero==2?.35f:.03f));
            Vector3 direction=(aim-origin).normalized;
            if(hero==1)
            {
                StartCoroutine(FlyBotShuriken(i,origin,direction,botStates[i].life,epoch.Value));
                return;
            }
            bool hit=NinjaShurikenFlight.FirstHit(origin,direction,35,view.transform,out var impact);
            // Recheck the actual spread ray: an ally can enter it after target selection.
            // Cancel before damage, interception or tracer effects, rather than merely ignoring damage.
            if(hit && TryBotCombatant(impact.collider,out var hitId) && !BotCanAttack(BotId(i),hitId))return;
            Vector3 end=hit?impact.point:origin+direction*35;
            if(WeaponController.TryInterceptBullet(origin,direction,hit?impact.distance:35,BotId(i),damage,true,out var blocked))end=blocked;
            else if(hit)impact.collider.GetComponentInParent<IHittable>()?.OnHit(new HitInfo {amount=damage,attackerId=BotId(i),hitPoint=impact.point,hitNormal=impact.normal});
            BotShotRpc(i,origin,end);
        }
        System.Collections.IEnumerator FlyBotShuriken(int i,Vector3 position,Vector3 direction,int life,int round)
        {
            bool predicted=NinjaShurikenFlight.FirstHit(position,direction,35,botViews[i].transform,out var endHit);
            BotShotRpc(i,position,predicted?endHit.point:position+direction*35);
            float remaining=35;
            while(remaining>0 && IsSpawned && CombatAllowed && epoch.Value==round && BotInSlot(i) && botStates[i].life==life && botStates[i].health>0)
            {
                float step=Mathf.Min(remaining,45*Time.deltaTime);
                bool hit=NinjaShurikenFlight.FirstHit(position,direction,step,botViews[i].transform,out var impact);
                if(hit && TryBotCombatant(impact.collider,out var id) && !BotCanAttack(BotId(i),id))yield break;
                if(WeaponController.TryInterceptBullet(position,direction,hit?impact.distance:step,BotId(i),18,true,out _))yield break;
                if(hit)
                {
                    impact.collider.GetComponentInParent<IHittable>()?.OnHit(new HitInfo{amount=18,attackerId=BotId(i),hitPoint=impact.point,hitNormal=impact.normal});
                    yield break;
                }
                position+=direction*step;remaining-=step;
                yield return null;
            }
        }
        System.Collections.IEnumerator BotShurikenBurst(int i,Vector3 target,ulong targetId,int life,int round)
        {
            for(int n=0;n<3;n++)
            {
                if(!HasAuthority || !CombatAllowed || epoch.Value!=round || !BotInSlot(i) || botStates[i].life!=life || botStates[i].health<=0 || botAmmo[i]<=0)yield break;
                botAmmo[i]--;FireBot(i,target,targetId);
                yield return new WaitForSeconds(.24f);
            }
        }
        [Rpc(SendTo.Everyone)]
        void BotReloadRpc(int i,RpcParams rpc=default)
        {if(rpc.Receive.SenderClientId==OwnerClientId && i>=0 && i<4 && botViews[i]!=null)botViews[i].PlayReload();}
        [Rpc(SendTo.Everyone)]
        void BotShotRpc(int i,Vector3 start,Vector3 end,RpcParams rpc=default)
        {
            if(rpc.Receive.SenderClientId!=OwnerClientId)return;
            if(i<0 || i>=4)return;
            if(botViews[i]!=null)botViews[i].PlayAttack(start,end);
        }
        public void HitBot(int i,HitInfo hit,int life)
        {
            if(!IsSpawned)return;
            if(HasAuthority)ApplyBotDamage(i,hit,life);
            else DamageBotRpc(i,hit,life,epoch.Value);
        }
        [Rpc(SendTo.Authority,InvokePermission=RpcInvokePermission.Everyone)]
        void DamageBotRpc(int i,HitInfo hit,int life,int version,RpcParams rpc=default)
        {
            if(!HasAuthority || version!=epoch.Value || hit.attackerId!=rpc.Receive.SenderClientId)return;
            ApplyBotDamage(i,hit,life);
        }
        void ApplyBotDamage(int i,HitInfo hit,int life)
        {
            if(!BotInSlot(i) || !CombatAllowed || !float.IsFinite(hit.amount) || hit.amount<=0 || RejectDamage(hit.attackerId,BotId(i)) || BotProtected(i))return;
            var state=botStates[i];if(state.health<=0 || state.life!=life)return;
            state.health=Mathf.Max(0,state.health-Mathf.Min(1000,hit.amount));
            if(state.health<=0 && state.character==2 && !state.pilot)
            {
                BotMechDestructRpc(i,state.position+Vector3.up,Now+3,epoch.Value);
                state.pilot=true;state.health=100;state.life++;
                var view=botViews[i];
                if(view!=null)
                {
                    // Keep the abandoned mech at the hit location while the pilot exits.
                    Vector3 exit=state.position-view.transform.forward*1.6f;
                    var path=new NavMeshPath();
                    if(view.Agent.enabled && view.Agent.isOnNavMesh &&
                        NavMesh.SamplePosition(exit,out var safe,1f,NavMesh.AllAreas) &&
                        view.Agent.CalculatePath(safe.position,path) && path.status==NavMeshPathStatus.PathComplete)
                    {
                        view.Agent.Warp(safe.position);
                        state.position=view.transform.position;
                    }
                }
                botAmmo[i]=7;botFireAt[i]=Time.time+.3f;
                botStates[i]=state;
                botViews[i]?.RenderState(state,HasAuthority,TeamMode?i%2:-1);
                return;
            }
            if(state.health<=0)
            {
                state.respawnAt=Now+3+(TeamMode?state.deaths:0);state.deaths++;
                if(!TeamMode)for(int j=0;j<slots.Count;j++)if(slots[j]==hit.attackerId && j!=i){killScores[j]++;break;}
            }
            botStates[i]=state;

        }
        void BotPayloadPresence(ref bool attack,ref bool defend,Vector3 center)
        {
            for(int i=0;i<4;i++)if(BotInSlot(i) && botStates[i].health>0 && !BotProtected(i))
            {
                var p=botStates[i].position;var delta=p-center;
                if(Mathf.Abs(delta.y)>2.5f || new Vector2(delta.x,delta.z).magnitude>escortRadius)continue;
                if(NinjaShurikenFlight.FirstHit(p+Vector3.up,(center-p).normalized,delta.magnitude,botViews[i].transform,out var obstruction) &&
                   (train==null || !obstruction.transform.IsChildOf(train)) &&
                   obstruction.collider.GetComponentInParent<ShooterTrainingBot>()==null &&
                   obstruction.collider.GetComponentInParent<WeaponController>()==null)continue;
                if(i%2==0)attack=true;else defend=true;
            }
        }
    }
}
