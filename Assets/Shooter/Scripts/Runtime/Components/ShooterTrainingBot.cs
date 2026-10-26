using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;
using Blocks.Gameplay.Core;

namespace Blocks.Gameplay.Shooter
{
    /// <summary>Local bot presentation. Match authority replicates state and resolves all decisions.</summary>
    public sealed partial class ShooterTrainingBot : MonoBehaviour, IHittable
    {
        ShooterEscortMatch match;
        int slot,life;
        public ulong ParticipantId => ShooterEscortMatch.BotId(slot);
        public NavMeshAgent Agent { get; private set; }
        public Transform Muzzle { get; private set; }
        Transform leftLeg,rightLeg;
        TextMesh label;
        Material armor,accent,weapon;
        Vector3 previousPosition;
        int hero=-1, attackSequence;
        float attackedAt=-100, reloadUntil;
        Transform rightArm,leftArm;
        Quaternion rightArmRest,leftArmRest;
        AudioSource actionAudio, fusionAudio;
        readonly System.Collections.Generic.List<GameObject> heroParts=new System.Collections.Generic.List<GameObject>();
        public static ShooterTrainingBot Create(ShooterEscortMatch match,int slot)
        {
            var root=new GameObject("AI_Character_"+(slot+1));
            root.SetActive(false);
            var bot=root.AddComponent<ShooterTrainingBot>();bot.match=match;bot.slot=slot;
            bot.Build();return bot;
        }
        void Build()
        {
            var shader=Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            armor=new Material(shader);armor.color=new Color(.16f,.21f,.28f);
            accent=new Material(shader);accent.color=new Color(.1f,.65f,1);
            weapon=new Material(shader);weapon.color=new Color(.055f,.065f,.08f);
            Part("Torso",new Vector3(0,1.18f,0),new Vector3(.58f,.65f,.32f),armor);
            Part("Helmet",new Vector3(0,1.71f,0),new Vector3(.36f,.37f,.34f),armor);
            Part("Visor",new Vector3(0,1.73f,.18f),new Vector3(.29f,.10f,.035f),accent);
            Part("Chest",new Vector3(0,1.28f,.18f),new Vector3(.37f,.2f,.035f),accent);
            Part("RightArm",new Vector3(.34f,1.16f,.12f),new Vector3(.17f,.42f,.21f),armor).localRotation=Quaternion.Euler(-35,0,0);
            Part("LeftArm",new Vector3(-.30f,1.18f,.20f),new Vector3(.17f,.46f,.20f),armor).localRotation=Quaternion.Euler(-60,0,-20);
            Part("Rifle",new Vector3(.16f,1.22f,.47f),new Vector3(.13f,.17f,.65f),weapon);
            Muzzle=Part("Muzzle",new Vector3(.16f,1.23f,.84f),new Vector3(.065f,.065f,.20f),weapon);
            leftLeg=Part("LeftLeg",new Vector3(-.17f,.47f,0),new Vector3(.22f,.86f,.25f),armor);
            rightLeg=Part("RightLeg",new Vector3(.17f,.47f,0),new Vector3(.22f,.86f,.25f),armor);
            var collider=gameObject.AddComponent<CapsuleCollider>();collider.center=Vector3.up*.95f;collider.height=1.9f;collider.radius=.36f;
            var nameplate=new GameObject("Name");nameplate.transform.SetParent(transform,false);nameplate.transform.localPosition=Vector3.up*2.2f;
            label=nameplate.AddComponent<TextMesh>();label.anchor=TextAnchor.MiddleCenter;label.alignment=TextAlignment.Center;
            label.characterSize=.045f;label.fontSize=48;
            Agent=gameObject.AddComponent<NavMeshAgent>();Agent.enabled=false;
            Agent.radius=.35f;Agent.height=1.8f;Agent.speed=4;Agent.angularSpeed=360;Agent.acceleration=15;Agent.stoppingDistance=.6f;
            Agent.updateRotation=false;Agent.avoidancePriority=40+slot;
            rightArm=transform.Find("RightArm");leftArm=transform.Find("LeftArm");
            rightArmRest=rightArm.localRotation;leftArmRest=leftArm.localRotation;
            actionAudio=gameObject.AddComponent<AudioSource>();actionAudio.playOnAwake=false;
            actionAudio.spatialBlend=1;actionAudio.rolloffMode=AudioRolloffMode.Linear;actionAudio.minDistance=2;actionAudio.maxDistance=30;
        }
        void Update()
        {
            if(fusionAudio!=null && (hero!=2 || match==null || !match.CombatAllowed || Time.time-attackedAt>.35f))
                fusionAudio.Stop();
        }
        void OnDisable()
        {
            if(fusionAudio!=null)fusionAudio.Stop();
            if(actionAudio!=null)actionAudio.Stop();
        }
        void SetHero(int character)
        {
            if(hero==character)return;
            if(fusionAudio!=null)fusionAudio.Stop();
            attackedAt=-100;reloadUntil=0;attackSequence=0;
            hero=character;
            foreach(var part in heroParts)if(part!=null)Destroy(part);heroParts.Clear();
            transform.Find("Rifle").gameObject.SetActive(hero==0);
            Muzzle.GetComponent<Renderer>().enabled=hero==0;
            armor.color=hero==1?new Color(.22f,.3f,.25f):hero==2?new Color(.95f,.25f,.55f):hero==3?new Color(.32f,.15f,.08f):new Color(.16f,.21f,.28f);
            transform.Find("Torso").localScale=hero==2?new Vector3(.85f,.8f,.6f):new Vector3(.58f,.65f,.32f);
            Agent.speed=hero==1?5.5f:hero==2?3.5f:4;
            if(hero==1)
            {
                AddHeroPart("Headband",new Vector3(0,1.78f,.19f),new Vector3(.39f,.06f,.05f),accent);
                AddHeroPart("BackSword",new Vector3(-.15f,1.3f,-.22f),new Vector3(.05f,1.05f,.06f),weapon).localRotation=Quaternion.Euler(0,0,-25);
                AddHeroPart("ShurikenGrip",new Vector3(.3f,1.28f,.44f),new Vector3(.18f,.02f,.04f),weapon);
                Muzzle.localPosition=new Vector3(.3f,1.28f,.5f);
            }
            if(hero==2)
            {
                AddHeroPart("LeftCannon",new Vector3(-.42f,1.22f,.44f),new Vector3(.22f,.25f,.7f),weapon);
                AddHeroPart("RightCannon",new Vector3(.42f,1.22f,.44f),new Vector3(.22f,.25f,.7f),weapon);
                AddHeroPart("Cockpit",new Vector3(0,1.5f,.29f),new Vector3(.5f,.3f,.12f),accent);
                Muzzle.localPosition=new Vector3(.42f,1.23f,.83f);
            }
            if(hero==3)
            {
                AddHeroPart("HatBrim",new Vector3(0,1.88f,0),new Vector3(.64f,.05f,.55f),weapon);
                AddHeroPart("HatCrown",new Vector3(0,1.97f,0),new Vector3(.37f,.16f,.34f),weapon);
                AddHeroPart("Poncho",new Vector3(-.16f,1.38f,.02f),new Vector3(.44f,.35f,.39f),armor);
                AddHeroPart("Revolver",new Vector3(.25f,1.26f,.51f),new Vector3(.09f,.10f,.36f),weapon);
                AddHeroPart("RevolverGrip",new Vector3(.25f,1.15f,.39f),new Vector3(.08f,.18f,.1f),armor);
                Muzzle.localPosition=new Vector3(.25f,1.27f,.72f);
            }
            if(hero==0)Muzzle.localPosition=new Vector3(.16f,1.23f,.84f);
            BuildCharacterModel();
        }
        Transform AddHeroPart(string name,Vector3 position,Vector3 size,Material material)
        {var part=Part(name,position,size,material);heroParts.Add(part.gameObject);return part;}
        public void PlayReload()
        {
            reloadUntil=Time.time+(hero==4?1.4f:hero==3?1.8f:1.5f);
            var clip=Resources.Load<AudioClip>(hero==1?"Audio/Genji/Reload":hero==3?"Audio/Cassidy/Reload":"");
            if(clip!=null)actionAudio.PlayOneShot(clip,.5f);
        }
        public void PlayAttack(Vector3 start,Vector3 end)
        {
            attackedAt=Time.time;attackSequence++;
            if(hero==2)
            {
                if(fusionAudio==null)
                {
                    fusionAudio=gameObject.AddComponent<AudioSource>();
                    fusionAudio.playOnAwake=false;fusionAudio.loop=true;fusionAudio.volume=.45f;
                    fusionAudio.spatialBlend=1;fusionAudio.rolloffMode=AudioRolloffMode.Linear;
                    fusionAudio.minDistance=2;fusionAudio.maxDistance=35;fusionAudio.dopplerLevel=0;
                    fusionAudio.clip=Resources.Load<AudioClip>("Audio/Dva/FusionCannons");
                }
                if(!fusionAudio.isPlaying && fusionAudio.clip!=null)fusionAudio.Play();
            }
            var clip=hero==4?Resources.Load<AudioClip>("Audio/Dva/PistolShot"):hero==1?Resources.Load<AudioClip>("Audio/Genji/Shot"):hero==3?Resources.Load<AudioClip>("Audio/Cassidy/Shot"+((attackSequence-1)%6%5+1)):null;
            if(clip!=null)actionAudio.PlayOneShot(clip,.5f);
            if(hero==1)StartCoroutine(DrawShuriken(start,end));else DrawShot(start,end);
            if(hero==2 || hero==4)
            {
                var shader=Shader.Find("Universal Render Pipeline/Unlit");
                if(shader!=null)
                {
                    var flash=GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    flash.name="AI Dva muzzle flash";
                    flash.GetComponent<Collider>().enabled=false;
                    flash.transform.position=start;
                    if((end-start).sqrMagnitude>.0001f)flash.transform.rotation=Quaternion.LookRotation(end-start);
                    flash.transform.localScale=new Vector3(.12f,.12f,.22f);
                    var mat=new Material(shader);mat.color=new Color(.25f,1,.72f);
                    flash.GetComponent<Renderer>().sharedMaterial=mat;
                    flash.GetComponent<Renderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                    Destroy(flash,.09f);Destroy(mat,.12f);
                }
            }
            if(hero==0 || hero==3)HeroAttackVisuals.Flash(start,end-start,new Color(1,.65f,.2f));
            if(hero==2 && characterModel==null)Muzzle.localPosition=new Vector3(attackSequence%2==0?.42f:-.42f,1.23f,.83f);
        }
        System.Collections.IEnumerator DrawShuriken(Vector3 start,Vector3 end)
        {
            var prefab=Resources.Load<GameObject>("Ninja/Shuriken");if(prefab==null)yield break;
            var star=Instantiate(prefab,start,Quaternion.LookRotation(end-start));
            foreach(var c in star.GetComponentsInChildren<Collider>())c.enabled=false;
            var r=star.GetComponentsInChildren<Renderer>();
            if(r.Length>0){var b=r[0].bounds;foreach(var v in r)b.Encapsulate(v.bounds);star.transform.localScale*=.16f/Mathf.Max(.001f,b.size.magnitude);}
            HeroAttackVisuals.Trail(star,new Color(.35f,1,.65f));
            Destroy(star,2f);
            while(star!=null && Vector3.Distance(star.transform.position,end)>.02f)
            {star.transform.position=Vector3.MoveTowards(star.transform.position,end,45*Time.deltaTime);star.transform.Rotate(0,0,1300*Time.deltaTime);yield return null;}
            if(star!=null)Destroy(star);
        }
        Transform Part(string name,Vector3 position,Vector3 scale,Material material)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(transform,false);
            go.transform.localPosition=position;go.transform.localScale=scale;
            var collider=go.GetComponent<Collider>();collider.enabled=false;Destroy(collider);
            go.GetComponent<Renderer>().sharedMaterial=material;return go.transform;
        }
        public void RenderState(ShooterBotState state,bool authority,int team)
        {
            life=state.life;
            SetHero(state.pilot?4:state.character);
            if(!authority)
            {
                Agent.enabled=false;
                transform.position=Vector3.Distance(transform.position,state.position)>3?state.position:Vector3.Lerp(transform.position,state.position,Time.deltaTime*14);
                transform.rotation=Quaternion.Slerp(transform.rotation,Quaternion.Euler(0,state.yaw,0),Time.deltaTime*14);
            }
            else if(Agent.enabled && Agent.isOnNavMesh)Agent.isStopped=!match.CombatAllowed;
            accent.color=team==0?new Color(.1f,.6f,1):team==1?new Color(1,.15f,.2f):new Color(1,.6f,.1f);
            label.color=accent.color;label.text="["+match.BotDifficultyName+"] AI "+ShooterEscortMatch.BotHeroName(hero)+" "+(slot+1)+"\n"+Mathf.CeilToInt(state.health)+" / "+ShooterEscortMatch.BotMaxHealth(hero);
            float kick=Mathf.Sin(Mathf.Clamp01((Time.time-attackedAt)/.24f)*Mathf.PI);
            rightArm.localRotation=rightArmRest*Quaternion.Euler(hero==1?-65*kick:-15*kick,0,0);
            leftArm.localRotation=leftArmRest*Quaternion.Euler(hero==1?25*kick:hero==2?-15*kick:0,0,0);
            if(Time.time<reloadUntil)rightArm.localRotation=Quaternion.Euler(15,0,-15);
            if(Camera.main!=null)label.transform.rotation=Camera.main.transform.rotation;
            bool moving=(transform.position-previousPosition).sqrMagnitude>.00001f;
            float swing=moving?Mathf.Sin(Time.time*10)*24:0;
            leftLeg.localRotation=Quaternion.Euler(swing,0,0);rightLeg.localRotation=Quaternion.Euler(-swing,0,0);
            previousPosition=transform.position;
            walkSwing=swing;
        }
        public void OnHit(HitInfo info) {match?.HitBot(slot,info,life);}
        public void SubmitHitRpc(HitInfo info,RpcParams rpcParams=default) {OnHit(info);}
        public static void DrawShot(Vector3 start,Vector3 end)
        {
            var shader=Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");if(shader==null)return;
            var go=new GameObject("AI bullet tracer");var line=go.AddComponent<LineRenderer>();
            var material=new Material(shader);material.color=new Color(1,.7f,.2f);line.material=material;
            line.positionCount=2;line.SetPosition(0,start);line.SetPosition(1,end);line.startWidth=.04f;line.endWidth=.01f;
            Destroy(go,.10f);Destroy(material,.13f);
        }
        void OnDestroy(){foreach(var material in modelMaterials)if(material!=null)Destroy(material);if(armor!=null)Destroy(armor);if(accent!=null)Destroy(accent);if(weapon!=null)Destroy(weapon);}
    }
}
