using System.Linq;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using Unity.Netcode;
using Unity.Services.Multiplayer;
using Blocks.Gameplay.Core;

namespace Blocks.Gameplay.Shooter
{
    // Only this object's authority advances the match (Relay or distributed authority).
    public sealed partial class ShooterEscortMatch : NetworkBehaviour
    {
        public static ShooterEscortMatch Instance { get; private set; }
        public GameObject teamMap;
        public Transform train;
        public Transform[] route;
        public Transform[] attackSpawns, defendSpawns;
        public GameObject[] teamOnlyDisabledObjects;
        [Min(30)] public float roundSeconds = 300;
        [Header("개인전")]
        [Min(30)] public float freeForAllSeconds = 300;
        [Min(.1f)] public float payloadSpeed = .75f;
        [Min(1)] public float escortRadius = 3.5f;
        readonly NetworkVariable<bool> escort = new NetworkVariable<bool>();
        readonly NetworkVariable<int> phase = new NetworkVariable<int>(); // 0 lobby, 1 countdown, 2 live, 4 result, 6 loading
        readonly NetworkVariable<float> distance = new NetworkVariable<float>();
        readonly NetworkVariable<double> deadline = new NetworkVariable<double>();
        readonly NetworkVariable<int> winner = new NetworkVariable<int>(-1);
        readonly NetworkVariable<bool> contested = new NetworkVariable<bool>();
        readonly NetworkVariable<int> epoch = new NetworkVariable<int>();
        NetworkList<ulong> slots;
        NetworkList<int> killScores;
        NetworkList<FixedString64Bytes> scoreNames;
        readonly Dictionary<ulong,int> countedEliminations = new Dictionary<ulong,int>();
        SessionObserver observer;
        CorePlayerState localState;
        int localEpoch = -1;
        float length;
        Rigidbody trainBody;
        float renderedDistance, distanceVelocity;
        bool trainPositionInitialized;
        bool initialized, wasTeamMode;
        bool[] originalActive;
        readonly Bounds[] spawnSafeAreas = new Bounds[2];
        Material blueSpawnMaterial, redSpawnMaterial;
        int savedQuality = -1;
        float performanceElapsed;
        int performanceFrames;
        string performanceText = "FPS 측정 중";
        const ulong Empty = ulong.MaxValue;
        double Now => NetworkManager.ServerTime.Time;
        public bool TeamMode => IsSpawned && escort.Value;
        public bool CombatAllowed => IsSpawned && phase.Value == 2;
        public int TeamOf(ulong id) { if (slots == null) return -1; for(int i=0;i<slots.Count;i++) if(slots[i]==id)return i%2;return -1; }
        public static bool RejectDamage(ulong attacker, ulong victim)
        {
            var m=Instance;
            if(m==null)return false;
            // Protection belongs only to the victim inside their own team's spawn.
            // A protected attacker must not make an enemy immune to damage.
            if(m.IsSpawnProtected(victim))return true;
            return !m.CombatAllowed || m.TeamOf(attacker)<0 || m.TeamOf(victim)<0 ||
                (m.TeamMode && m.TeamOf(attacker)==m.TeamOf(victim));
        }
        [Header("힐팩")]
        [Min(1)] public float healthPackHealing = 50f;
        [Min(1)] public float healthPackRespawnSeconds = 15f;
        public Vector3[] healthPackPositions = {
            new Vector3(-12,.2f,-20), new Vector3(12,.2f,-20),
            new Vector3(-12,.2f,-12), new Vector3(12,.2f,-12) };
        NetworkList<double> healthPackReadyAt;
        GameObject[] healthPackVisuals;
        Material healthPackMaterial;
        float nextHealthPackScan;

        void CreateHealthPacks()
        {
            healthPackVisuals=new GameObject[healthPackPositions.Length];
            var shader=Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            if(shader==null)return;
            healthPackMaterial=new Material(shader);
            healthPackMaterial.color=new Color(.1f,1f,.45f);
            healthPackMaterial.SetColor("_BaseColor",healthPackMaterial.color);
            for(int i=0;i<healthPackVisuals.Length;i++)
            {
                var root=new GameObject("HealthPack_"+(i+1));
                root.transform.SetParent(transform,false);
                root.transform.position=healthPackPositions[i];
                healthPackVisuals[i]=root;
                for(int part=0;part<3;part++)
                {
                    var mesh=GameObject.CreatePrimitive(PrimitiveType.Cube);
                    mesh.name=part==0?"Pad":"HealingCross";
                    mesh.transform.SetParent(root.transform,false);
                    mesh.transform.localPosition=Vector3.up*(part==0?.04f:.75f);
                    mesh.transform.localScale=part==0?new Vector3(.9f,.08f,.9f):
                        part==1?new Vector3(.65f,.2f,.2f):new Vector3(.2f,.65f,.2f);
                    var collider=mesh.GetComponent<Collider>();collider.enabled=false;Destroy(collider);
                    mesh.GetComponent<Renderer>().sharedMaterial=healthPackMaterial;
                }
                root.SetActive(false);
            }
        }

        void UpdateHealthPacks()
        {
            bool live=IsSpawned && CombatAllowed;
            if(IsSpawned && HasAuthority)
                while(healthPackReadyAt.Count<healthPackPositions.Length)healthPackReadyAt.Add(0);
            for(int i=0;i<healthPackVisuals.Length;i++)
                if(healthPackVisuals[i]!=null)
                    healthPackVisuals[i].SetActive(live && i<healthPackReadyAt.Count && Now>=healthPackReadyAt[i]);
            if(!live || !HasAuthority || Time.unscaledTime<nextHealthPackScan)return;
            nextHealthPackScan=Time.unscaledTime+.1f;
            for(int i=0;i<healthPackPositions.Length;i++)
            {
                if(Now<healthPackReadyAt[i])continue;
                foreach(var player in WeaponController.ActivePlayers)
                {
                    if(player==null || !player.IsSpawned || TeamOf(player.OwnerClientId)<0)continue;
                    var state=player.GetComponent<CorePlayerState>();
                    var stats=player.GetComponent<CoreStatsHandler>();
                    if(state==null || !state.IsActive || stats==null || !stats.IsAlive ||
                        stats.GetCurrentValue(StatKeys.Health)>=stats.GetMaxValue(StatKeys.Health))continue;
                    var delta=player.transform.position-healthPackPositions[i];
                    if(Mathf.Abs(delta.y)>1.5f || new Vector2(delta.x,delta.z).sqrMagnitude>1.44f)continue;
                    Vector3 origin=player.transform.position+Vector3.up*.7f;
                    Vector3 target=healthPackPositions[i]+Vector3.up*.7f;
                    if(NinjaShurikenFlight.FirstHit(origin,(target-origin).normalized,Vector3.Distance(origin,target),player.transform,out _))continue;
                    // Reserve before delivery so simultaneous contenders cannot consume twice.
                    healthPackReadyAt[i]=Now+healthPackRespawnSeconds;
                    GrantHealthPackRpc(player.NetworkObjectId,epoch.Value);
                    break;
                }
            }
        }

        [Rpc(SendTo.Everyone)]
        void GrantHealthPackRpc(ulong playerObjectId,int round,RpcParams rpc=default)
        {
            if(rpc.Receive.SenderClientId!=OwnerClientId || round!=epoch.Value || !CombatAllowed)return;
            if(!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(playerObjectId,out var player) || !player.IsOwner)return;
            var state=player.GetComponent<CorePlayerState>();
            var stats=player.GetComponent<CoreStatsHandler>();
            if(state!=null && state.IsActive && stats!=null && stats.IsAlive)
                stats.ModifyStat(StatKeys.Health,healthPackHealing);
        }
        void Awake()
        {
            InitializeBots();
            InitializeSettings();
            healthPackReadyAt=new NetworkList<double>();
            CreateHealthPacks();
            CreateSpawnSafeAreas();
            slots=new NetworkList<ulong>();
            killScores=new NetworkList<int>();
            scoreNames=new NetworkList<FixedString64Bytes>();
            readyPlayers=new NetworkList<ulong>();
            loadedPlayers=new NetworkList<ulong>();
            lobbyChat=new NetworkList<FixedString512Bytes>();
            teamOnlyDisabledObjects ??= System.Array.Empty<GameObject>();
            route ??= System.Array.Empty<Transform>();
            originalActive=teamOnlyDisabledObjects.Select(o=>o!=null&&o.activeSelf).ToArray();
            if(teamMap!=null)teamMap.SetActive(false);
            for(int i=1;i<route.Length;i++)length+=Vector3.Distance(route[i-1].position,route[i].position);
            if(train!=null)
            {
                trainBody=train.GetComponent<Rigidbody>();
                if(trainBody==null)trainBody=train.gameObject.AddComponent<Rigidbody>();
                trainBody.isKinematic=true;
                trainBody.useGravity=false;
                trainBody.interpolation=RigidbodyInterpolation.Interpolate;
                trainBody.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;
                // A flat wall down to the floor prevents the controller stepping onto
                // the old floating lower edge every time the cart moves into it.
                var body=train.GetComponent<BoxCollider>();
                if(body!=null){body.center=new Vector3(0,.85f,0);body.size=new Vector3(1.85f,1.9f,3.5f);}
            }
        }
        public override void OnNetworkSpawn()
        {
            Instance=this;
            ResetBots();
            observer=new SessionObserver("default-session");
            if(HasAuthority)
            {
                if(NetworkManager.DistributedAuthorityMode && IsSessionOwner)
                    NetworkObject.SetOwnershipStatus(NetworkObject.OwnershipStatus.SessionOwner, true);
                slots.Clear();for(int i=0;i<4;i++)slots.Add(Empty);
                killScores.Clear();scoreNames.Clear();countedEliminations.Clear();
                for(int i=0;i<4;i++){killScores.Add(0);scoreNames.Add(default);}
                readyPlayers.Clear();loadedPlayers.Clear();phase.Value=0;
                lobbyChat.Clear();chatLastSent.Clear();
                distance.Value=0;winner.Value=-1;epoch.Value=0;escort.Value=false;
                contested.Value=false;
            }
        }
        public override void OnNetworkDespawn()
        {
            StopLobbyMusic();
            StopMatchMusic();
            CleanupBots();
            chatDraft="";chatLastSent.Clear();chatScroll=Vector2.zero;chatSeenCount=0;
            ReleaseLobbyInput();
            if(localState!=null)
            {
                var motor=localState.GetComponent<CoreMovement>();
                motor.RespawnPointOverride=null;
                motor.RespawnDelayOverride=-1;
                motor.RespawnDelayPerDeath=1;
            }
            observer?.Dispose();observer=null;
            if(localState!=null)localState.OnLifeStateChanged-=OnLifeChanged;
            localState=null;localEpoch=-1;initialized=false;
            if(teamMap!=null)teamMap.SetActive(false);
            for(int i=0;i<teamOnlyDisabledObjects.Length;i++)if(teamOnlyDisabledObjects[i]!=null)teamOnlyDisabledObjects[i].SetActive(originalActive[i]);
            wasTeamMode=false;
            trainPositionInitialized=false;distanceVelocity=0;
            if(Instance==this)Instance=null;
        }
        void Update()
        {
            UpdateLobbyMusic();
            UpdateMatchMusic();
            UpdatePreparationMusic();
            UpdateEscapeMenu();
            UpdateHealthPacks();
            UpdatePerformanceControls();
            if(!IsSpawned || NetworkManager==null || NetworkManager.ShutdownInProgress || observer==null)return;
            UpdateBots();
            var session=observer.Session;
            if(HasAuthority&&!initialized&&session!=null)
            {
                escort.Value=session.Properties.TryGetValue("shooter-mode",out var mode)&&mode.Value=="escort";
                initialized=true;
            }
            if(TeamMode!=wasTeamMode)
            {
                wasTeamMode=TeamMode;
                if(teamMap!=null)teamMap.SetActive(TeamMode);
                for(int i=0;i<teamOnlyDisabledObjects.Length;i++)if(teamOnlyDisabledObjects[i]!=null)teamOnlyDisabledObjects[i].SetActive(TeamMode?false:originalActive[i]);
            }
            if(!HasAuthority || !initialized)return;
            var players=WeaponController.ActivePlayers.Where(p=>p!=null && p.IsSpawned).ToArray();
            if(phase.Value==0)
            {
                for(int i=0;i<slots.Count;i++)if(slots[i]!=Empty&&!IsBotId(slots[i])&&!players.Any(p=>p.OwnerClientId==slots[i]))
                {readyPlayers.Remove(slots[i]);slots[i]=Empty;}
                foreach(var p in players.OrderBy(p=>p.OwnerClientId))
                    if(TeamOf(p.OwnerClientId)<0)for(int i=0;i<4;i++)if(slots[i]==Empty||BotInSlot(i)){readyPlayers.Remove(slots[i]);slots[i]=p.OwnerClientId;break;}
                for(int i=0;i<slots.Count;i++)
                {
                    if(BotInSlot(i))continue;
                    var player=players.FirstOrDefault(p=>p.OwnerClientId==slots[i]);
                    string name=player!=null?player.GetComponent<CorePlayerState>().PlayerName:"";
                    // Keep UTF-8 names within the fixed-string network payload.
                    while(System.Text.Encoding.UTF8.GetByteCount(name)>60)name=name.Substring(0,name.Length-1);
                    var networkName=new FixedString64Bytes(name);
                    if(!scoreNames[i].Equals(networkName))scoreNames[i]=networkName;
                }
                FillBotSlots();
                return;
            }
            if(phase.Value==6) {UpdateLoading(players);return;}
            if(phase.Value==1&&Now>=deadline.Value){phase.Value=2;deadline.Value=Now+(TeamMode?roundSeconds:freeForAllSeconds);}
            if(phase.Value!=2)return;
            if(!TeamMode)
            {
                if(Now>=deadline.Value){phase.Value=4;contested.Value=false;}
                return;
            }
            if(Now>=deadline.Value){FinishMatch(1);return;}
            bool attack=false,defend=false;
            Vector3 center=RoutePoint(distance.Value);
            foreach(var p in players)
            {
                if(!p.GetComponent<CoreStatsHandler>().IsAlive)continue;
                if(IsSpawnProtected(p.OwnerClientId))continue;
                Vector3 delta=p.transform.position-center;
                if(Mathf.Abs(delta.y)>2.5f||new Vector2(delta.x,delta.z).magnitude>escortRadius)continue;
                // Geometry between a player and the payload prevents pushing through walls.
                Vector3 eye=p.transform.position+Vector3.up;
                Vector3 target=center+Vector3.up;
                if(NinjaShurikenFlight.FirstHit(eye,(target-eye).normalized,Vector3.Distance(eye,target),p.transform,out var hit)&&!hit.transform.IsChildOf(teamMap.transform))continue;
                if(TeamOf(p.OwnerClientId)==0)attack=true;else if(TeamOf(p.OwnerClientId)==1)defend=true;
            }
            BotPayloadPresence(ref attack,ref defend,center);
            contested.Value=attack&&defend;
            if(attack&&!defend)distance.Value=Mathf.Min(length,distance.Value+payloadSpeed*Time.deltaTime);
            else if(defend&&!attack)distance.Value=Mathf.Max(0,distance.Value-payloadSpeed*Time.deltaTime);
            if(distance.Value>=length-.001f)FinishMatch(0);
        }
        public bool IsSpawnProtected(ulong playerId)
        {
            if(!TeamMode)return false;
            if(IsBotId(playerId))return BotProtected((int)(ulong.MaxValue-1-playerId));
            int team=TeamOf(playerId);
            if(team<0)return false;
            foreach(var player in WeaponController.ActivePlayers)
                if(player!=null && player.IsSpawned && player.OwnerClientId==playerId)
                    return player.GetComponent<CorePlayerState>().IsActive && spawnSafeAreas[team].Contains(player.transform.position);
            return false;
        }

        void CreateSpawnSafeAreas()
        {
            if(teamMap==null)return;
            if(blueSpawnMaterial!=null)Destroy(blueSpawnMaterial);
            if(redSpawnMaterial!=null)Destroy(redSpawnMaterial);
            for(int team=0;team<2;team++)
            {
                string areaName=team==0?"Blue_Attack_SpawnSafeArea":"Red_Defense_SpawnSafeArea";
                // Rebuilding the display must never leave a second visible boundary.
                for(int i=teamMap.transform.childCount-1;i>=0;i--)
                {
                    var previous=teamMap.transform.GetChild(i);
                    if(previous.name!=areaName)continue;
                    previous.gameObject.SetActive(false);
                    Destroy(previous.gameObject);
                }
                var points=team==0?attackSpawns:defendSpawns;
                if(points==null || points.Length==0)continue;
                var bounds=new Bounds(points[0].position,Vector3.zero);
                foreach(var point in points)if(point!=null)bounds.Encapsulate(point.position);
                bounds.Expand(new Vector3(4,6,3));
                spawnSafeAreas[team]=bounds;
                var root=new GameObject(areaName);
                root.transform.SetParent(teamMap.transform,false);
                var shader=Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                if(shader==null)continue;
                var material=new Material(shader);
                var color=team==0?new Color(.08f,.5f,1):new Color(1,.12f,.15f);
                material.SetColor("_BaseColor",color);material.color=color;
                if(team==0)blueSpawnMaterial=material;else redSpawnMaterial=material;
                // Flat strips identify the exact protected boundary without adding step edges.
                float y=points[0].position.y-.18f;
                for(int edge=0;edge<4;edge++)
                {
                    var marker=GameObject.CreatePrimitive(PrimitiveType.Cube);
                    marker.name="SafeAreaBoundary";marker.transform.SetParent(root.transform,false);
                    var collider=marker.GetComponent<Collider>();collider.enabled=false;Destroy(collider);
                    bool horizontal=edge<2;
                    marker.transform.position=new Vector3(horizontal?bounds.center.x:(edge==2?bounds.min.x:bounds.max.x),y,
                        horizontal?(edge==0?bounds.min.z:bounds.max.z):bounds.center.z);
                    marker.transform.localScale=horizontal?new Vector3(bounds.size.x,.02f,.12f):new Vector3(.12f,.02f,bounds.size.z);
                    marker.GetComponent<Renderer>().sharedMaterial=material;
                }
                var sign=new GameObject("SpawnSign");sign.transform.SetParent(root.transform,false);
                sign.transform.position=new Vector3(team==0?bounds.min.x:bounds.max.x,bounds.center.y+2.2f,bounds.center.z);
                sign.transform.rotation=Quaternion.Euler(0,team==0?90:-90,0);
                var label=sign.AddComponent<TextMesh>();label.text=team==0?"BLUE SPAWN\nSAFE ZONE":"RED SPAWN\nSAFE ZONE";
                label.anchor=TextAnchor.MiddleCenter;label.alignment=TextAlignment.Center;label.characterSize=.13f;label.fontSize=40;label.color=color;
            }
        }

        public override void OnDestroy()
        {
            CleanupBots();
            SaveLocalSettings();
            if(settingsVolume!=null)Destroy(settingsVolume.gameObject);
            if(settingsProfile!=null)Destroy(settingsProfile);
            if(healthPackMaterial!=null)Destroy(healthPackMaterial);
            RestorePerformanceQuality();
            if(blueSpawnMaterial!=null)Destroy(blueSpawnMaterial);
            if(redSpawnMaterial!=null)Destroy(redSpawnMaterial);
            base.OnDestroy();
        }

        void BeginPreparation()
        {
            for(int i=0;i<healthPackReadyAt.Count;i++)healthPackReadyAt[i]=0;
            for(int i=0;i<killScores.Count;i++)killScores[i]=0;
            countedEliminations.Clear();
            phase.Value=1;distance.Value=0;deadline.Value=Now+PreparationDuration;contested.Value=false;epoch.Value++;
        }
        public void ReportElimination(ulong attacker, int sequence, ulong victimObject)
        {
            if(!IsSpawned || TeamMode || !CombatAllowed)return;
            RecordEliminationRpc(attacker,sequence,victimObject,epoch.Value);
        }

        [Rpc(SendTo.Authority, InvokePermission=RpcInvokePermission.Everyone)]
        void RecordEliminationRpc(ulong attacker, int sequence, ulong victimObject, int version, RpcParams rpc=default)
        {
            ulong victim=rpc.Receive.SenderClientId;
            if(!HasAuthority || TeamMode || phase.Value!=2 || version!=epoch.Value || Now>=deadline.Value ||
                attacker==victim || TeamOf(victim)<0 || TeamOf(attacker)<0 || sequence<=0)return;
            if(!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(victimObject,out var obj) || obj.OwnerClientId!=victim ||
                obj.GetComponent<ShooterHitProcessor>()==null)return;
            if(countedEliminations.TryGetValue(victimObject,out var previous) && sequence<=previous)return;
            countedEliminations[victimObject]=sequence;
            for(int i=0;i<slots.Count;i++)if(slots[i]==attacker){killScores[i]++;break;}
        }

        void DrawFreeForAllScores()
        {
            var order=Enumerable.Range(0,slots.Count).Where(i=>slots[i]!=Empty).OrderByDescending(i=>killScores[i]).ToArray();
            var oldMatrix=GUI.matrix;
            float scale=Mathf.Min(1,Mathf.Min(Screen.width/800f,Screen.height/450f));
            GUI.matrix=Matrix4x4.Scale(Vector3.one*scale);
            float width=Screen.width/scale;
            int seconds=phase.Value==4?0:Mathf.CeilToInt(Mathf.Max(0,(float)(deadline.Value-Now)));
            var timerStyle=new GUIStyle(GUI.skin.label){fontSize=24,alignment=TextAnchor.MiddleCenter,fontStyle=FontStyle.Bold};
            timerStyle.normal.textColor=Color.white;
            GUI.Label(new Rect(width*.5f-82,10,164,36),phase.Value==4?"경기 종료":$"{seconds/60:00}:{seconds%60:00}",timerStyle);
            var rowStyle=new GUIStyle(GUI.skin.label){fontSize=14,clipping=TextClipping.Clip,wordWrap=false};
            rowStyle.normal.textColor=Color.white;
            var oldColor=GUI.color;
            GUI.color=oldColor;
            GUI.Label(new Rect(20,15,210,22),"개인전 · 킬 순위",rowStyle);
            int row=0;
            foreach(int i in order)
            {
                int rank=1+order.Count(j=>killScores[j]>killScores[i]);
                rowStyle.normal.textColor=slots[i]==NetworkManager.LocalClientId?new Color(.4f,.85f,1):Color.white;
                string name=ScoreName(i);
                if(name.Length>12)name=name.Substring(0,11)+"…";
                GUI.Label(new Rect(20,39+row*22,169,22),$"{rank}. {name}{(slots[i]==NetworkManager.LocalClientId?" (나)":"")}",rowStyle);
                GUI.Label(new Rect(194,39+row*22,44,22),$"{killScores[i]}킬",rowStyle);
                row++;
            }
            if(phase.Value==4 && order.Length>0)
            {
                int best=killScores[order[0]];
                var leaders=order.Where(i=>killScores[i]==best).ToArray();
                GUILayout.BeginArea(new Rect(width*.5f-230,65,460,130),GUI.skin.box);
                GUILayout.Label($"{(leaders.Length>1?"공동 1등":"1등")}: {string.Join(", ",leaders.Select(ScoreName))} · {best}킬");
                GUILayout.EndArea();
            }
            GUI.matrix=oldMatrix;
        }
        string ScoreName(int index)=>scoreNames[index].IsEmpty?$"플레이어 {index+1}":scoreNames[index].ToString();
        void FinishMatch(int winningTeam)
        {
            winner.Value=winningTeam;phase.Value=4;contested.Value=false;
        }
        Vector3 RoutePoint(float d)
        {
            for(int i=1;i<route.Length;i++){float segment=Vector3.Distance(route[i-1].position,route[i].position);if(d<=segment)return Vector3.Lerp(route[i-1].position,route[i].position,d/Mathf.Max(segment,.001f));d-=segment;}
            return route[route.Length-1].position;
        }
        void FixedUpdate()
        {
            if(!TeamMode || trainBody==null || route.Length<2)return;
            bool snap=!trainPositionInitialized || distance.Value<renderedDistance-.5f;
            renderedDistance=snap?distance.Value:Mathf.SmoothDamp(renderedDistance,distance.Value,ref distanceVelocity,.08f,Mathf.Infinity,Time.fixedDeltaTime);
            var p=RoutePoint(renderedDistance);
            var forward=RoutePoint(Mathf.Min(length,renderedDistance+.3f))-RoutePoint(Mathf.Max(0,renderedDistance-.3f));
            var rotation=forward.sqrMagnitude>.0001f?Quaternion.LookRotation(forward):trainBody.rotation;
            if(snap)
            {
                trainBody.position=p;trainBody.rotation=rotation;
                distanceVelocity=0;trainPositionInitialized=true;
            }
            else
            {
                trainBody.MovePosition(p);
                trainBody.MoveRotation(Quaternion.RotateTowards(trainBody.rotation,rotation,90*Time.fixedDeltaTime));
            }
        }
        void LateUpdate()
        {
            if(!IsSpawned)return;
            if(localState==null)
            {
                var owner=WeaponController.ActivePlayers.FirstOrDefault(p=>p!=null&&p.IsOwner&&p.IsSpawned);
                localState=owner!=null?owner.GetComponent<CorePlayerState>():null;
                if(localState!=null)localState.OnLifeStateChanged+=OnLifeChanged;
            }
            UpdateLobbyInput();
            if(localState==null||TeamOf(localState.OwnerClientId)<0)return;
            localState.GetComponent<CoreMovement>().RespawnPointOverride=TeamMode?GetFixedTeamSpawn():null;
            localState.GetComponent<CoreMovement>().RespawnDelayOverride=3;
            localState.GetComponent<CoreMovement>().RespawnDelayPerDeath=TeamMode?1:0;
            if(localEpoch!=epoch.Value){localEpoch=epoch.Value;ResetLocalPlayer();}
            if(phase.Value==1)MoveToTeamSpawn();
            AcknowledgeLoading();
        }
        void OnLifeChanged(PlayerLifeState state){if(state==PlayerLifeState.Respawned&&TeamMode)MoveToTeamSpawn();}
        void ResetLocalPlayer()
        {
            localState.GetComponent<CoreMovement>().MatchDeathCount=0;
            var stats=localState.GetComponent<CoreStatsHandler>();
            stats.ModifyStat(StatKeys.Health,stats.GetMaxValue(StatKeys.Health),localState.OwnerClientId,ModificationSource.Regeneration);
            if(!localState.IsActive)localState.SetLifeState(PlayerLifeState.Respawned);
            localState.GetComponent<WeaponController>().RestoreDvaOnRespawn();
            MoveToTeamSpawn();
        }
        void MoveToTeamSpawn()
        {
            if(!TeamMode)return;
            var spawn=GetFixedTeamSpawn();
            if(spawn==null)return;
            var motor=localState.GetComponent<CoreMovement>();
            motor.RespawnPointOverride=spawn;
            motor.transform.rotation=spawn.rotation;motor.SetPosition(spawn.position);motor.ResetMovementForces();
        }
        Transform GetFixedTeamSpawn()
        {
            if(localState==null || TeamOf(localState.OwnerClientId)<0)return null;
            int index=0;for(int i=0;i<slots.Count;i++)if(slots[i]==localState.OwnerClientId)index=i/2;
            var points=TeamOf(localState.OwnerClientId)==0?attackSpawns:defendSpawns;
            if(points==null || points.Length==0)return null;
            return points[index%points.Length];
        }
        readonly BotanicalMatchResult resultArt = new BotanicalMatchResult();
        void OnGUI()
        {
            if(leavingRoom || !string.IsNullOrEmpty(exitError)){DrawRoomExitStatus();return;}
            if(escapeMenuOpen){DrawEscapeMenu();return;}
            if(!IsSpawned)return;
            if(phase.Value!=4) resultArt.Reset();
            if(phase.Value==0 || phase.Value==6 || phase.Value==1) {DrawLobby();return;}
            if(!TeamMode){DrawFreeForAllScores();}
            else
            {
            string team=TeamOf(NetworkManager.LocalClientId)==0?"블루팀 · 공격":"레드팀 · 수비";
            string text=phase.Value==4?(winner.Value==0?"블루팀 승리 · 화물 운송 완료":"레드팀 승리 · 제한 시간 종료"):
                $"{team} | 남은 시간 {Mathf.Max(0,(float)(deadline.Value-Now)):0}초 | 화물 {distance.Value:0.0}/{length:0.0}m {(contested.Value?"경합 · 운송 정지":"")}";
            var hudStyle=new GUIStyle(GUI.skin.label){alignment=TextAnchor.MiddleCenter,fontSize=16};
            hudStyle.normal.textColor=Color.white;
            GUI.Label(new Rect(Screen.width*.2f,12,Screen.width*.6f,28),text,hudStyle);
            if(IsSpawnProtected(NetworkManager.LocalClientId))
                GUI.Label(new Rect(Screen.width*.3f,42,Screen.width*.4f,24),"아군 스폰 · 보호 중",hudStyle);
            }
            if(phase.Value==4)
            {
                bool won = TeamMode ? winner.Value == TeamOf(NetworkManager.LocalClientId) :
                    Enumerable.Range(0,slots.Count).Any(i => slots[i]==NetworkManager.LocalClientId &&
                        !Enumerable.Range(0,slots.Count).Any(j => slots[j]!=Empty && killScores[j]>killScores[i]));
                resultArt.Draw(won);
                GUILayout.BeginArea(new Rect(Screen.width*.5f-140,Screen.height*.80f,280,100),GUI.skin.box);
                if(HasAuthority && GUILayout.Button("대기방으로 돌아가기",GUILayout.Height(36)))
                {
                    phase.Value=0;distance.Value=0;winner.Value=-1;
                    readyPlayers.Clear();loadedPlayers.Clear();epoch.Value++;UnlockRoom();
                }
                if(GUILayout.Button("방 나가기",GUILayout.Height(36)))LeaveLobby();
                GUILayout.EndArea();
            }
            DrawPerformanceControls();
            if(phase.Value==2 && localState!=null && !localState.IsActive)
            {
                var weapons=localState.GetComponent<WeaponController>();
                GUILayout.BeginArea(new Rect(Screen.width*.5f-220,Screen.height*.58f,440,110),GUI.skin.box);
                GUILayout.Label("부활할 캐릭터 선택");
                GUILayout.BeginHorizontal();
                string[] names={"솔저","닌자","디바","캐서디"};
                for(int i=0;i<names.Length;i++)
                    if(GUILayout.Button((weapons.RespawnCharacter==i?"✓ ":"")+names[i],GUILayout.Height(40)))weapons.QueueRespawnCharacter(i);
                GUILayout.EndHorizontal();GUILayout.EndArea();
            }
        }

        void UpdatePerformanceControls()
        {
            if(UnityEngine.InputSystem.Keyboard.current?.f8Key.wasPressedThisFrame==true)TogglePerformanceQuality();
            performanceElapsed+=Time.unscaledDeltaTime;performanceFrames++;
            if(performanceElapsed<.5f)return;
            float fps=performanceFrames/Mathf.Max(performanceElapsed,.001f);
            string connection=NetworkManager!=null && NetworkManager.IsConnectedClient?"연결됨":"미접속 / 연결 대기";
            performanceText=$"{fps:0} FPS · {connection}";
            performanceElapsed=0;performanceFrames=0;
        }
        void TogglePerformanceQuality()
        {
            if(savedQuality>=0){RestorePerformanceQuality();return;}
            savedQuality=QualitySettings.GetQualityLevel();
            int low=System.Array.IndexOf(QualitySettings.names,"Mobile");
            QualitySettings.SetQualityLevel(low>=0?low:0,true);
        }
        void RestorePerformanceQuality()
        {
            if(savedQuality<0)return;
            QualitySettings.SetQualityLevel(savedQuality,true);savedQuality=-1;
        }
        void DrawPerformanceControls()
        {
            int oldDepth=GUI.depth;GUI.depth=-1100;
            float width=Mathf.Min(250,Screen.width*.32f);
            GUI.Label(new Rect(Screen.width-width-10,10,width,22),performanceText);
            string label=savedQuality>=0?"F8 · 저부하 모드 켜짐 (원래 화질로)":"F8 · 저부하 모드";
            if(Cursor.lockState!=CursorLockMode.Locked)
            {
                if(GUI.Button(new Rect(Screen.width-width-10,34,width,26),label))TogglePerformanceQuality();
            }
            else GUI.Label(new Rect(Screen.width-width-10,34,width,26),label);
            GUI.depth=oldDepth;
        }
    }
}
