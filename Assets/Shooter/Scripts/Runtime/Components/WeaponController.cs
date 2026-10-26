using UnityEngine;
using Unity.Netcode;
using System.Collections;
using Blocks.Gameplay.Core;
using Unity.Netcode.Components;
using System.Collections.Generic;
using System.Linq;

namespace Blocks.Gameplay.Shooter
{
    /// <summary>
    /// Manages weapon switching, attachment, and firing for networked characters.
    /// Handles weapon loadout spawning, state synchronization, and animation integration.
    /// This component works with <see cref="AimController"/> and synchronizes weapon state across the network.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public partial class WeaponController : NetworkBehaviour
    {
        internal static readonly HashSet<WeaponController> ActivePlayers = new HashSet<WeaponController>();
        public override void OnDestroy()
        {
            ActivePlayers.Remove(this);
            base.OnDestroy();
        }
        #region Fields & Properties

        [Header("Component Dependencies")]
        [Tooltip("Reference to the AimController that manages aiming behavior and aim target positions.")]
        [SerializeField] private AimController aimController;
        [Tooltip("Reference to the CoreStatsHandler for checking player alive state.")]
        [SerializeField] private CoreStatsHandler coreStats;

        [Header("Loadout Configuration")]
        [Tooltip("The weapon loadout configuration containing weapon prefabs and default weapon index.")]
        [SerializeField] private WeaponLoadout weaponLoadout;

        [Header("Listening to Events")]
        [Tooltip("Event raised when the primary action (fire) button is pressed.")]
        [SerializeField] private GameEvent onPrimaryActionPressedEvent;
        [Tooltip("Event raised when the primary action (fire) button is released.")]
        [SerializeField] private GameEvent onPrimaryActionReleasedEvent;
        [Tooltip("Event raised when the reload button is pressed.")]
        [SerializeField] private GameEvent onReloadPressedEvent;
        [Tooltip("Event raised when the next weapon button is pressed.")]
        [SerializeField] private GameEvent onNextWeaponPressedEvent;
        [Tooltip("Event raised when the previous weapon button is pressed.")]
        [SerializeField] private GameEvent onPreviousWeaponPressedEvent;
        [Tooltip("Event raised when the aiming state changes.")]
        [SerializeField] private BoolEvent onAimingStateChanged;

        [Header("Broadcasting on")]
        [Tooltip("Event raised when the active weapon changes, containing old and new weapon references.")]
        [SerializeField] private WeaponSwapEvent onWeaponChanged;

        private readonly List<AttachableBehaviour> m_SpawnedWeaponAttachables = new List<AttachableBehaviour>();
        private readonly List<IWeapon> m_WeaponImplementations = new List<IWeapon>();
        private readonly List<string> m_WeaponPrefabNames = new List<string>();
        private readonly Dictionary<string, AttachableNode> m_AttachmentNodes = new Dictionary<string, AttachableNode>();
        private readonly float m_WeaponSwitchAnimationSyncTime = 0.45f;
        private readonly NetworkVariable<PlayerWeaponState> m_PlayerWeaponState = new NetworkVariable<PlayerWeaponState>(
            new PlayerWeaponState(),
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner
        );

        private readonly NetworkVariable<int> m_CharacterId = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public int CharacterId => m_CharacterId.Value;
        int queuedRespawnCharacter=-1;
        public int RespawnCharacter => queuedRespawnCharacter>=0?queuedRespawnCharacter:CharacterId;
        public void SelectLobbyCharacter(int character)
        {
            if (!IsOwner || !IsSpawned || character < 0 || character > 3 ||
                ShooterEscortMatch.Instance == null || !ShooterEscortMatch.Instance.CanSelectLobbyCharacter) return;
            queuedRespawnCharacter = character;
            ApplyRespawnCharacter();
        }
        public void QueueRespawnCharacter(int character)
        {
            if(IsOwner && IsSpawned && !GetComponent<CorePlayerState>().IsActive && character>=0 && character<4)
                queuedRespawnCharacter=character;
        }
        public void ApplyRespawnCharacter()
        {
            if(!IsOwner || !IsSpawned)return;
            m_ShurikenAmmo.Value = ShurikenMagazineSize;
            m_ShurikenReloading.Value = false;
            if(queuedRespawnCharacter>=0)
            {
                StopAllCoroutines();
                m_LoadoutSpawnCoroutine = null;
                m_WeaponSwitchCoroutine = null;
                m_RevolverFanning=false;m_RevolverReloading.Value=false;
                m_RevolverAmmo.Value=6;m_RevolverNextShot=0;
                m_NinjaKatana.Value=false;m_Parry.Value=false;
                m_KatanaDashEnds=0;m_NinjaAttackEnds=0;
                m_MatrixActive.Value=false;m_DvaTrigger=false;
                m_DvaBoostUntil=0;m_DvaBoostVfxActive.Value=false;
                CancelDeadeye();
                m_CharacterId.Value=queuedRespawnCharacter;queuedRespawnCharacter=-1;
                GetComponent<CoreMovement>().UsesExternalSprintStamina=CharacterId==2;
            }
            RestoreDvaOnRespawn();
            EnsureSoldierLoadout();
        }

        private IWeapon m_CurrentWeapon;
        private AttachableBehaviour m_CurrentAttachableWeapon;
        private int m_CurrentWeaponIndex;
        private int m_AnimIDWeaponType;
        private bool m_IsAiming;
        private Animator m_Animator;
        private Coroutine m_WeaponSwitchCoroutine;
        private Coroutine m_LoadoutSpawnCoroutine;

        private void EnsureSoldierLoadout()
        {
            if (!IsSpawned || !IsOwner || CharacterId != 0 || m_LoadoutSpawnCoroutine != null ||
                weaponLoadout == null || weaponLoadout.weaponPrefabs.Count == 0) return;
            m_LoadoutSpawnCoroutine = StartCoroutine(SpawnLoadoutWeaponsCoroutine());
        }

        static readonly int k_IsSwitchingWeapon = Animator.StringToHash("IsSwitchingWeapon");
        static readonly int k_AnimIDIsReloading = Animator.StringToHash("IsReloading");

        /// <summary>
        /// Gets the currently equipped weapon implementation.
        /// </summary>
        public IWeapon CurrentWeapon => m_CurrentWeapon;

        /// <summary>
        /// Gets the index of the currently equipped weapon in the weapon list.
        /// </summary>
        public int CurrentWeaponIndex => m_CurrentWeaponIndex;

        /// <summary>
        /// Gets the total number of weapons in the loadout.
        /// </summary>
        public int WeaponCount => m_WeaponImplementations?.Count ?? 0;

        #endregion

        #region Unity Methods

        private float m_SoldierRapidFireUntil;
        private float m_SoldierMissileReady;
        private AudioClip m_SoldierMissileClip;
        public float SoldierMissileCooldownRemaining => Mathf.Max(0, m_SoldierMissileReady - Time.time);

        private void UpdateSoldierMissile()
        {
            if (!IsSpawned || !IsOwner || CharacterId != 0 || coreStats == null || !coreStats.IsAlive ||
                Cursor.lockState != CursorLockMode.Locked || Camera.main == null || SoldierMissileCooldownRemaining > 0 ||
                UnityEngine.InputSystem.Mouse.current == null || !UnityEngine.InputSystem.Mouse.current.rightButton.wasPressedThisFrame ||
                (ShooterEscortMatch.Instance?.IsSpawnProtected(OwnerClientId) ?? false)) return;
            if (!(CurrentWeapon is ModularWeapon rifle)) return;
            PositionSoldierView();
            var view = Camera.main.transform;
            Vector3 target = view.position + view.forward * 100;
            if (NinjaShurikenFlight.FirstHit(view.position, view.forward, 100, transform, out var hit)) target = hit.point;
            Vector3 origin = rifle.Muzzle != null ? rifle.Muzzle.position : view.TransformPoint(new Vector3(.2f,-.15f,.6f));
            Vector3 offset = origin-view.position;
            if (offset.sqrMagnitude > .001f && NinjaShurikenFlight.FirstHit(view.position, offset.normalized, offset.magnitude, transform, out _)) origin=view.position;
            Vector3 direction = target-origin;
            if (direction.sqrMagnitude < .001f) direction=view.forward;
            m_SoldierMissileReady=Time.time+6;
            SoldierMissileRpc(origin,direction.normalized);
        }

        [Rpc(SendTo.Everyone)]
        private void SoldierMissileRpc(Vector3 origin, Vector3 direction, RpcParams rpc=default)
        {
            if (rpc.Receive.SenderClientId != OwnerClientId || CharacterId != 0 || direction.sqrMagnitude < .01f) return;
            if(m_SoldierMissileClip==null)m_SoldierMissileClip=Resources.Load<AudioClip>("Audio/Soldier/HelixRockets_User");
            if(m_SoldierMissileClip!=null)
            {
                var sound=new GameObject("Soldier Helix launch audio");
                sound.transform.position=origin;
                var audio=sound.AddComponent<AudioSource>();
                audio.playOnAwake=false;audio.clip=m_SoldierMissileClip;audio.volume=.8f;
                audio.spatialBlend=IsOwner?0:1;
                audio.rolloffMode=AudioRolloffMode.Linear;audio.minDistance=3;audio.maxDistance=50;audio.dopplerLevel=0;
                audio.Play();Destroy(sound,m_SoldierMissileClip.length+.1f);
            }
            StartCoroutine(FlySoldierMissile(origin,direction.normalized));
        }

        private IEnumerator FlySoldierMissile(Vector3 origin, Vector3 direction)
        {
            var source=Resources.Load<GameObject>("Soldier/Rocket");
            if(source==null)yield break;
            var rocket=new GameObject("Soldier Missile");
            rocket.transform.SetPositionAndRotation(origin,Quaternion.identity);
            var model=Instantiate(source,rocket.transform,false);
            foreach(var c in model.GetComponentsInChildren<Collider>())c.enabled=false;
            var renderers=model.GetComponentsInChildren<Renderer>();
            Bounds bounds=new Bounds(origin,Vector3.zero);
            if(renderers.Length>0)
            {
                bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
                float longest=Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z);
                if(longest>.001f)
                {
                    float scale=.65f/longest;
                    Vector3 axis=bounds.size.x>=bounds.size.y && bounds.size.x>=bounds.size.z?Vector3.right:
                        bounds.size.y>=bounds.size.z?Vector3.up:Vector3.forward;
                    model.transform.localRotation=Quaternion.FromToRotation(axis,Vector3.forward);
                    model.transform.localScale*=scale;
                    model.transform.localPosition=-(model.transform.localRotation*(bounds.center-origin))*scale;
                }
            }
            rocket.transform.rotation=Quaternion.LookRotation(direction);
            var trail=CreateDvaQuickEffect("Boost",Color.white,true);
            if(trail!=null)
            {
                trail.transform.SetParent(rocket.transform,false);
                trail.transform.localPosition=new Vector3(0,0,-.25f);
                trail.transform.localRotation=Quaternion.Euler(0,180,0);
                trail.transform.localScale=Vector3.one*.10f;
                foreach(var ps in trail.GetComponentsInChildren<ParticleSystem>())
                {var main=ps.main;main.loop=true;main.maxParticles=48;}
                SetDvaEffectActive(trail,true);
            }
            Destroy(rocket,4f);
            float travelled=0;
            while(rocket!=null && IsSpawned && travelled<100)
            {
                float step=Mathf.Min(45*Time.deltaTime,100-travelled);
                bool hit=NinjaShurikenFlight.FirstHit(rocket.transform.position,direction,step,transform,out var impact);
                if(TryBlockDvaMatrix(rocket.transform.position,direction,hit?impact.distance:step,OwnerClientId,out _))break;
                if(hit)
                {
                    Vector3 blast=impact.point+impact.normal*.03f;
                    if(IsOwner)DamageSoldierMissile(blast);
                    var effect=CreateDvaQuickEffect("SelfDestructCore",Color.white,true);
                    if(effect!=null){effect.transform.SetParent(null,false);effect.transform.position=blast;effect.transform.localScale=Vector3.one*.24f;SetDvaEffectActive(effect,true);Destroy(effect,5f);}
                    break;
                }
                rocket.transform.position+=direction*step;travelled+=step;
                yield return null;
            }
            if(rocket!=null)Destroy(rocket);
        }

        private void DamageSoldierMissile(Vector3 origin)
        {
            if(ShooterEscortMatch.Instance!=null && !ShooterEscortMatch.Instance.CombatAllowed)return;
            var damaged=new HashSet<IHittable>();
            foreach(var collider in Physics.OverlapSphere(origin,3f,~0,QueryTriggerInteraction.Ignore))
            {
                if(collider.transform.IsChildOf(transform))continue;
                var target=collider.GetComponentInParent<IHittable>();
                if(target==null || damaged.Contains(target))continue;
                Vector3 point=collider.ClosestPoint(origin),delta=point-origin;
                if(delta.sqrMagnitude>.0001f && NinjaShurikenFlight.FirstHit(origin,delta.normalized,delta.magnitude+.02f,transform,out var block) && block.collider.GetComponentInParent<IHittable>()!=target)continue;
                damaged.Add(target);
                target.OnHit(new HitInfo{amount=Mathf.Lerp(120,30,Mathf.Clamp01(delta.magnitude/3)),attackerId=OwnerClientId,hitPoint=point,hitNormal=-delta.normalized,impactForce=delta.normalized*25});
            }
        }
        private float m_SoldierRapidReadyAt;
        public float SoldierRapidCooldownRemaining => Mathf.Max(0, m_SoldierRapidReadyAt - Mathf.Max(Time.time, m_SoldierRapidFireUntil));
        private AutomaticMechanism m_SoldierRapidMechanism;
        public float SoldierRapidFireRemaining => Mathf.Max(0, m_SoldierRapidFireUntil - Time.time);
        public bool SoldierInfiniteAmmo => IsSpawned && isActiveAndEnabled && IsOwner && CharacterId == 0 &&
            coreStats != null && coreStats.IsAlive && SoldierRapidFireRemaining > 0;
        private float SoldierMovementMultiplier() => IsSpawned && isActiveAndEnabled && CharacterId == 0 &&
            coreStats != null && coreStats.IsAlive && SoldierRapidFireRemaining > 0 ? 2f : 1f;
        private void OnDisable()
        {
            StopDvaAttackAudio();
            if (m_DvaBoostAudio != null) m_DvaBoostAudio.Stop();
            RestoreCassidyRollPose();
            StopRevolverAudio();
            RestoreNinjaDashBody();
            m_DashSlashStarted = -100;
            m_KatanaDashEnds = 0;
            m_SoldierRapidFireUntil = 0;
            if (m_SoldierRapidMechanism != null) m_SoldierRapidMechanism.SetRateMultiplier(1);
        }
        private void UpdateSoldierRapidFire()
        {
            bool usable = CharacterId == 0 && coreStats != null && coreStats.IsAlive;
            if (!usable) m_SoldierRapidFireUntil = 0;
            var mechanism = usable && CurrentWeapon is ModularWeapon weapon ? weapon.GetComponent<AutomaticMechanism>() : null;
            if (m_SoldierRapidMechanism != mechanism)
            {
                if (m_SoldierRapidMechanism != null) m_SoldierRapidMechanism.SetRateMultiplier(1);
                m_SoldierRapidMechanism = mechanism;
            }
            if (mechanism == null) return;
            if (Time.time >= m_SoldierRapidReadyAt && SoldierRapidFireRemaining <= 0 && Cursor.lockState == CursorLockMode.Locked &&
                UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.eKey.wasPressedThisFrame)
            {
                m_SoldierRapidFireUntil = Time.time + 5f;
                m_SoldierRapidReadyAt = m_SoldierRapidFireUntil + 7.5f;
                var boostedWeapon = CurrentWeapon;
                mechanism.GetComponent<AmmoHandler>()?.BeginInfiniteAmmo(() => this != null &&
                    SoldierInfiniteAmmo && CurrentWeapon == boostedWeapon);
            }
            mechanism.SetRateMultiplier(SoldierRapidFireRemaining > 0 ? 2f : 1f);
        }

        [Header("Soldier FPS View")]
        [SerializeField] private Vector3 soldierViewPosition = new Vector3(0.23f, -0.22f, 0.22f);
        [SerializeField] private Vector3 soldierViewRotation = new Vector3(0, -90, -7);
        private GameObject m_SoldierView;
        private ModularWeapon m_SoldierViewSource;
        private readonly Dictionary<Renderer, UnityEngine.Rendering.ShadowCastingMode> m_SoldierOriginalShadows = new Dictionary<Renderer, UnityEngine.Rendering.ShadowCastingMode>();
        private void ClearSoldierView()
        {
            if (m_SoldierViewSource != null) { m_SoldierViewSource.SetFirstPersonPoints(null, null); m_SoldierViewSource.RefreshFirstPersonPose = null; }
            foreach (var pair in m_SoldierOriginalShadows) if (pair.Key != null) pair.Key.shadowCastingMode = pair.Value;
            m_SoldierOriginalShadows.Clear();
            if (m_SoldierView != null)
            {
                m_SoldierView.SetActive(false);
                Destroy(m_SoldierView);
            }
            m_SoldierView = null;
            m_SoldierViewSource = null;
        }
        private Transform CopySoldierPoint(Transform original, Transform source)
        {
            if (original == null) return null;
            var point = new GameObject(original.name + "_FPS").transform;
            point.SetParent(m_SoldierView.transform, false);
            point.localPosition = Quaternion.Inverse(source.rotation) * (original.position - source.position);
            point.localRotation = Quaternion.Inverse(source.rotation) * original.rotation;
            return point;
        }
        private SkinnedMeshRenderer m_SoldierBodyRenderer;
        private Mesh m_OriginalBodyMesh;
        private Material[] m_OriginalBodyMaterials;
        private SkinnedMeshRenderer m_SoldierBodyAsset;
        private bool m_SoldierBodyLoadAttempted, m_SoldierBodyApplied;
        [System.Serializable] public class SoldierHoldBone { public string name; public Quaternion rotation; }
        [System.Serializable] public class SoldierHoldPose { public SoldierHoldBone[] bones; }
        private SoldierHoldPose m_SoldierHoldPose;
        private Transform[] m_SoldierHoldBones;
        private bool m_SoldierHoldLoaded;
        private void ApplySoldierHoldPose()
        {
            if(!IsSpawned || CharacterId!=0 || m_Animator==null || coreStats==null || !coreStats.IsAlive ||
                m_Animator.GetBool(k_AnimIDIsReloading) || m_Animator.GetBool(k_IsSwitchingWeapon))return;
            if(!m_SoldierHoldLoaded)
            {
                m_SoldierHoldLoaded=true;
                var asset=Resources.Load<TextAsset>("Soldier/HoldPose");
                if(asset==null)return;
                m_SoldierHoldPose=JsonUtility.FromJson<SoldierHoldPose>(asset.text);
                var transforms=m_Animator.GetComponentsInChildren<Transform>(true);
                m_SoldierHoldBones=m_SoldierHoldPose.bones.Select(b=>transforms.FirstOrDefault(t=>t.name==b.name)).ToArray();
            }
            if(m_SoldierHoldPose==null)return;
            for(int i=0;i<m_SoldierHoldBones.Length;i++)
                if(m_SoldierHoldBones[i]!=null)m_SoldierHoldBones[i].localRotation=m_SoldierHoldPose.bones[i].rotation;
            if(!m_Animator.isHuman)return;
            // Remote players do not populate the owner's loadout/CurrentWeapon.
            // Resolve the shared hand socket independently on every client.
            string socket=m_CurrentWeapon?.GetWeaponData()?.handAttachmentNodeName ?? "Right_Hand_Attach";
            if(m_AttachmentNodes.TryGetValue(socket,out var node))
            {
                Vector3 direction=transform.forward;
                if(aimController!=null)
                {
                    Vector3 target=aimController.PreciseAimTargetPosition;
                    if(target!=Vector3.zero)
                    {
                        // Solve using the world muzzle, not the camera or wrist origin.
                        // Its position changes as the rifle turns around the grip.
                        for(int i=0;i<4;i++)
                        {
                            direction=target-node.transform.TransformPoint(new Vector3(.7462f,.1505f,.0004f));
                            AlignSoldierRiflePose(m_Animator,transform,node.transform,direction);
                        }
                        return;
                    }
                }
                AlignSoldierRiflePose(m_Animator,transform,node.transform,direction);
            }
        }
        // The rifle is authored along +X, not Unity's +Z. Solve hands around that
        // actual attachment frame instead of treating the sampled clip as a complete pose.
        public static void AlignSoldierRiflePose(Animator animator, Transform root, Transform attachment, Vector3 direction)
        {
            if(animator==null || !animator.isHuman || attachment==null || direction.sqrMagnitude<.0001f)return;
            var right=animator.GetBoneTransform(HumanBodyBones.RightHand);
            var left=animator.GetBoneTransform(HumanBodyBones.LeftHand);
            if(right==null || left==null || !attachment.IsChildOf(right))return;
            // Blender-calibrated receiver grip; the socket is not the wrist pivot.
            attachment.localPosition=new Vector3(0,.10f,0);
            var aim=Quaternion.LookRotation(direction.normalized,root.up);
            var frame=aim*Quaternion.Euler(0,-90,0);
            var rightRotation=frame*Quaternion.Inverse(attachment.rotation)*right.rotation;
            var leftRotation=frame*Quaternion.Inverse(attachment.rotation)*left.rotation;
            var leftUpper=animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            var leftLower=animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            if(leftUpper==null || leftLower==null)return;
            float reach=Vector3.Distance(leftUpper.position,leftLower.position)+Vector3.Distance(leftLower.position,left.position)-.003f;
            Vector3 grip=root.TransformPoint(new Vector3(.14f,1.40f,.21f));
            Vector3 support=Vector3.zero;
            // Keep both hands on the rifle when aiming up/down: move the shared
            // weapon frame back inside the support arm's reach before solving it.
            for(int i=0;i<4;i++)
            {
                SolveArm(animator,root,HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm,HumanBodyBones.RightHand,root.InverseTransformPoint(grip),1);
                right.rotation=rightRotation;
                support=attachment.TransformPoint(new Vector3(.30f,0,.06f));
                Vector3 delta=support-leftUpper.position;
                if(delta.magnitude<=reach || i==3)break;
                grip-=delta.normalized*(delta.magnitude-reach);
            }
            SolveArm(animator,root,HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.LeftHand,root.InverseTransformPoint(support),-1);
            left.rotation=leftRotation;
        }
        [SerializeField, Range(0,.15f)] private float soldierShoulderClearance = .04f;
        private readonly Transform[] m_SoldierShoulders = new Transform[2];
        private readonly Vector3[] m_SoldierShoulderOffsets = new Vector3[2];
        private void RestoreSoldierShoulders()
        {
            for(int i=0;i<2;i++)
            {
                if(m_SoldierShoulders[i]!=null)m_SoldierShoulders[i].localPosition-=m_SoldierShoulderOffsets[i];
                m_SoldierShoulderOffsets[i]=Vector3.zero;
            }
        }
        private void FitSoldierShoulders()
        {
            if(!IsSpawned || CharacterId!=0 || !m_SoldierBodyApplied || m_Animator==null || !m_Animator.isHuman || coreStats==null || !coreStats.IsAlive)return;
            for(int i=0;i<2;i++)
            {
                if(m_SoldierShoulders[i]==null)m_SoldierShoulders[i]=m_Animator.GetBoneTransform(i==0?HumanBodyBones.LeftUpperArm:HumanBodyBones.RightUpperArm);
                var bone=m_SoldierShoulders[i];
                if(bone==null || bone.parent==null)continue;
                // Keep the animated rotations; move the arm chain outside the wider chest armour.
                var offset=bone.parent.InverseTransformVector(transform.right*(i==0?-1:1)*Mathf.Min(soldierShoulderClearance,.04f));
                bone.localPosition+=offset;
                m_SoldierShoulderOffsets[i]=offset;
            }
        }
        private void UpdateSoldierBody()
        {
            if(!IsSpawned)return;
            if(!m_SoldierBodyLoadAttempted)
            {
                m_SoldierBodyLoadAttempted=true;
                // Reuse the existing renderer and bones, preserving animation, camera hiding and hitboxes.
                m_SoldierBodyRenderer=GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .FirstOrDefault(r=>r.name=="Armature_Mesh");
                var asset=Resources.Load<GameObject>("Soldier/SoldierBody");
                if(asset!=null)m_SoldierBodyAsset=asset.GetComponent<SkinnedMeshRenderer>();
                if(m_SoldierBodyRenderer!=null)
                {
                    m_OriginalBodyMesh=m_SoldierBodyRenderer.sharedMesh;
                    m_OriginalBodyMaterials=m_SoldierBodyRenderer.sharedMaterials;
                }
                if(m_SoldierBodyRenderer==null || m_SoldierBodyAsset==null)
                    Debug.LogWarning("Soldier body asset or original skeleton is missing; keeping original character.",this);
            }
            if(m_SoldierBodyRenderer==null)return;
            bool useSoldier=CharacterId==0;
            var selected=useSoldier?m_SoldierBodyAsset:null;
            var mesh=selected!=null?selected.sharedMesh:m_OriginalBodyMesh;
            if(m_SoldierBodyRenderer.sharedMesh==mesh)return;
            m_SoldierBodyRenderer.sharedMesh=mesh;
            m_SoldierBodyRenderer.sharedMaterials=selected!=null?selected.sharedMaterials:m_OriginalBodyMaterials;
            m_SoldierBodyApplied=useSoldier;
        }
        private void PositionSoldierView()
        {
            if (!IsOwner || !IsSpawned || CharacterId != 0)
            {
                if(m_SoldierView!=null)ClearSoldierView();
                return;
            }
            var source = CurrentWeapon as ModularWeapon;
            if(source==null)
            {
                ClearSoldierView();
                return;
            }
            if (source != m_SoldierViewSource)
            {
                ClearSoldierView();
                if (source != null)
                {
                    m_SoldierViewSource = source;
                    m_SoldierView = new GameObject("Soldier_FirstPersonWeapon");
                    Transform viewMuzzle = CopySoldierPoint(source.Muzzle, source.transform);
                    Transform viewShell = CopySoldierPoint(source.ShellEjection, source.transform);
                    source.SetFirstPersonPoints(viewMuzzle, viewShell);
                    source.RefreshFirstPersonPose = PositionSoldierView;
                    foreach (var filter in source.GetComponentsInChildren<MeshFilter>())
                    {
                        var original = filter.GetComponent<MeshRenderer>();
                        if (original == null || filter.sharedMesh == null) continue;
                        var part = new GameObject(filter.name);
                        part.transform.SetParent(m_SoldierView.transform, false);
                        part.transform.localPosition = Quaternion.Inverse(source.transform.rotation) * (filter.transform.position - source.transform.position);
                        part.transform.localRotation = Quaternion.Inverse(source.transform.rotation) * filter.transform.rotation;
                        part.transform.localScale = filter.transform.lossyScale;
                        part.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                        var renderer = part.AddComponent<MeshRenderer>(); renderer.sharedMaterials = original.sharedMaterials;
                        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        m_SoldierOriginalShadows[original] = original.shadowCastingMode;
                        original.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
                    }
                }
            }
            if (m_SoldierView == null || Camera.main == null) return;
            m_SoldierView.SetActive(coreStats != null && coreStats.IsAlive);
            var view = Camera.main.transform;
            bool reload = source.GetCurrentState() == WeaponState.Reloading;
            m_SoldierView.transform.SetPositionAndRotation(view.TransformPoint(soldierViewPosition + (reload ? Vector3.down * .12f : Vector3.zero)),
                view.rotation * Quaternion.Euler(soldierViewRotation + (reload ? new Vector3(0,0,-25) : Vector3.zero)));
            if (!reload && source.Muzzle != null)
            {
                Vector3 target = view.position + view.forward * 100f;
                if (NinjaShurikenFlight.FirstHit(view.position, view.forward, 100f, transform, out var hit)) target = hit.point;
                // The rifle's authored barrel axis is +X, not the Transform's +Z.
                // Rotate the whole view model, then recompute because its muzzle moves with it.
                for (int i = 0; i < 4; i++)
                {
                    Vector3 direction = target - source.Muzzle.position;
                    if (Vector3.Dot(direction, view.forward) <= 0.05f) break;
                    m_SoldierView.transform.rotation = Quaternion.FromToRotation(m_SoldierView.transform.right, direction.normalized)
                        * m_SoldierView.transform.rotation;
                }
                source.Muzzle.rotation = Quaternion.LookRotation(m_SoldierView.transform.right, view.up);
            }
        }

        private void Awake()
        {
            m_Animator = GetComponentInChildren<Animator>();
            m_AnimIDWeaponType = Animator.StringToHash("WeaponTypeID");

            var allNodes = GetComponentsInChildren<AttachableNode>(true);
            foreach (var node in allNodes)
            {
                if (!m_AttachmentNodes.TryAdd(node.gameObject.name, node))
                {
                    Debug.LogWarning($"Duplicate AttachableNode name found: {node.gameObject.name}. Only the first one will be used.", this);
                }
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            ActivePlayers.Add(this);
            if (IsOwner) m_CharacterId.Value = ShooterCharacterSelection.SelectedCharacter;

            if (IsOwner)
            {
                var movement = GetComponent<CoreMovement>();
                if (movement != null) movement.LocomotionSpeedMultiplier = SoldierMovementMultiplier;
                onPrimaryActionPressedEvent.RegisterListener(HandleFirePressed);
                onPrimaryActionReleasedEvent.RegisterListener(HandleFireReleased);
                onReloadPressedEvent.RegisterListener(HandleReloadPressed);
                onNextWeaponPressedEvent.RegisterListener(HandleNextWeaponPressed);
                onPreviousWeaponPressedEvent.RegisterListener(HandlePreviousWeaponPressed);
                onAimingStateChanged.RegisterListener(HandleAimingStateChanged);

                m_PlayerWeaponState.Value = new PlayerWeaponState { hasWeapon = false, isAiming = false, weaponIndex = 0 };

                if (CharacterId == 0 && weaponLoadout != null && weaponLoadout.weaponPrefabs.Count > 0)
                {
                    EnsureSoldierLoadout();
                }
            }

            InitializeNinjaWeapons();
            InitializeDva();
            InitializeRevolver();
            InitializeCassidyRoll();
            ApplyPlayerWeaponState();
            m_PlayerWeaponState.OnValueChanged += OnPlayerWeaponStateChanged;
        }

        public override void OnNetworkDespawn()
        {
            StopAllCoroutines();
            m_LoadoutSpawnCoroutine = null;
            m_WeaponSwitchCoroutine = null;
            CleanupCassidyRoll();
            RestoreSoldierShoulders();
            if(m_SoldierBodyRenderer!=null && m_OriginalBodyMesh!=null)
            {
                m_SoldierBodyRenderer.sharedMesh=m_OriginalBodyMesh;
                m_SoldierBodyRenderer.sharedMaterials=m_OriginalBodyMaterials;
            }
            m_SoldierBodyApplied=false;
            m_SoldierBodyLoadAttempted=false;
            ActivePlayers.Remove(this);
            if (m_SoldierRapidMechanism != null) m_SoldierRapidMechanism.SetRateMultiplier(1);
            m_SoldierRapidFireUntil = 0;
            if (IsOwner)
            {
                var movement = GetComponent<CoreMovement>();
                if (movement != null && movement.LocomotionSpeedMultiplier == SoldierMovementMultiplier)
                    movement.LocomotionSpeedMultiplier = null;
            }
            ClearSoldierView();
            CleanupRevolver();
            CleanupDva();
            CleanupNinjaWeapons();
            if (IsOwner)
            {
                onPrimaryActionPressedEvent.UnregisterListener(HandleFirePressed);
                onPrimaryActionReleasedEvent.UnregisterListener(HandleFireReleased);
                onReloadPressedEvent.UnregisterListener(HandleReloadPressed);
                onNextWeaponPressedEvent.UnregisterListener(HandleNextWeaponPressed);
                onPreviousWeaponPressedEvent.UnregisterListener(HandlePreviousWeaponPressed);
                onAimingStateChanged.UnregisterListener(HandleAimingStateChanged);

                foreach (var attachableWeapon in m_SpawnedWeaponAttachables)
                {
                    if (attachableWeapon != null && attachableWeapon.NetworkObject != null && attachableWeapon.NetworkObject.IsSpawned)
                    {
                        attachableWeapon.NetworkObject.Despawn();
                    }
                }
            }

            m_SpawnedWeaponAttachables.Clear();
            m_WeaponImplementations.Clear();
            m_WeaponPrefabNames.Clear();
            m_PlayerWeaponState.OnValueChanged -= OnPlayerWeaponStateChanged;

            base.OnNetworkDespawn();
        }

        private void Update()
        {
            RestoreCassidyRollPose();
            RestoreNinjaDashBody();
            // Remove last frame's correction before Animator/rigging evaluates the next pose.
            RestoreSoldierShoulders();
            if (!IsOwner) return;
            UpdateNinjaAwakening();
            UpdateCassidyRoll();
            UpdateDeadeye();
            if (ShooterEscortMatch.Instance != null && !ShooterEscortMatch.Instance.CombatAllowed)
            {
                HandleFireReleased();
                return;
            }
            if (CharacterId == 0 && coreStats != null && coreStats.IsAlive && CurrentWeapon is ModularWeapon soldierWeapon &&
                soldierWeapon.GetCurrentState() == WeaponState.Empty) soldierWeapon.TryReload();
            UpdateSoldierRapidFire();
            UpdateSoldierMissile();
            UpdateDva();
            UpdateRevolver();
            UpdateNinjaWeaponInput();
            UpdatePlayerWeaponState();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Checks if the specified weapon prefab is already in the spawned weapon list.
        /// </summary>
        /// <param name="weaponPrefab">The weapon prefab GameObject to check.</param>
        /// <returns>True if the weapon prefab is already spawned, false otherwise.</returns>
        public bool HasWeaponPrefab(GameObject weaponPrefab)
        {
            if (weaponPrefab == null || m_WeaponPrefabNames == null)
                return false;

            string weaponPrefabName = weaponPrefab.name.Replace("(Clone)", "").Trim();

            foreach (var existingWeaponName in m_WeaponPrefabNames)
            {
                if (string.Equals(weaponPrefabName, existingWeaponName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Gets the weapon index from the current player weapon state.
        /// </summary>
        /// <returns>The current weapon index.</returns>
        public int GetWeaponIndex()
        {
            return m_PlayerWeaponState.Value.weaponIndex;
        }

        /// <summary>
        /// Sets the active state of the current weapon's visual components.
        /// </summary>
        /// <param name="isActive">Whether the weapon should be active or inactive.</param>
        public void SetCurrentWeaponActive(bool isActive)
        {
            if (m_CurrentAttachableWeapon != null)
            {
                var weaponObjectController = m_CurrentAttachableWeapon.GetComponent<ComponentController>();
                if (weaponObjectController != null)
                {
                    weaponObjectController.SetEnabled(isActive);
                }
            }
        }

        #endregion

        #region Private Methods

        private void HandleAimingStateChanged(bool isAiming)
        {
            m_IsAiming = isAiming;

            if (!isAiming && CharacterId != 0)
            {
                m_CurrentWeapon?.StopFiring();
            }

            if (m_CurrentAttachableWeapon != null && m_CurrentAttachableWeapon.HasAuthority)
            {
                m_CurrentAttachableWeapon.Detach();
            }

            UpdateWeaponAttachment();
        }

        private void HandleNextWeaponPressed()
        {
            if (CurrentWeapon == null || !coreStats.IsAlive || CurrentWeapon.GetCurrentState() == WeaponState.Reloading || WeaponCount <= 1) return;
            if (aimController != null && aimController.IsWeaponObstructed) return;
            SwitchToWeapon((m_CurrentWeaponIndex + 1) % WeaponCount);
        }

        private void HandlePreviousWeaponPressed()
        {
            if (CurrentWeapon == null || !coreStats.IsAlive || CurrentWeapon.GetCurrentState() == WeaponState.Reloading || WeaponCount <= 1) return;
            if (aimController != null && aimController.IsWeaponObstructed) return;
            SwitchToWeapon((m_CurrentWeaponIndex - 1 + WeaponCount) % WeaponCount);
        }

        private void HandleFirePressed()
        {
            if (ShooterEscortMatch.Instance != null && !ShooterEscortMatch.Instance.CombatAllowed) return;
            if (CharacterId == 3) { FireRevolverSingle(); return; }
            if (CharacterId == 2) { if (DvaPilot) FireDvaPilot(); else m_DvaTrigger = true; return; }
            if (CharacterId == 1) { TryNinjaAttack(); return; }
            if (!coreStats.IsAlive ||
                CurrentWeapon == null ||
                !CurrentWeapon.CanFire() ||
                Cursor.lockState != CursorLockMode.Locked ||
                m_Animator.GetBool(k_IsSwitchingWeapon) ||
                m_Animator.GetBool(k_AnimIDIsReloading)) return;

            Vector3 fireOrigin = Camera.main != null ? Camera.main.transform.position : transform.position;
            Vector3 aimTargetPosition = aimController.PreciseAimTargetPosition;

            Transform muzzleTransform = (CurrentWeapon as ModularWeapon)?.Muzzle ?? aimController.AimTransform;
            Vector3 fireDirection = (aimTargetPosition - muzzleTransform.position).normalized;

            CurrentWeapon.Fire(gameObject, fireOrigin, fireDirection);
        }

        private void HandleFireReleased()
        {
            if (IsOwner && IsSpawned && CharacterId == 2) StopDvaAttackAudioRpc();
            m_DvaTrigger = false;
            m_CurrentWeapon?.StopFiring();
        }

        private void HandleReloadPressed()
        {
            if (CharacterId == 1) { ReloadShurikens(); return; }
            if (CharacterId == 2) { ReloadDvaPilot(); return; }
            if (CharacterId == 3) { ReloadRevolver(); return; }
            if (!coreStats.IsAlive || CurrentWeapon == null || CurrentWeapon.GetCurrentState() == WeaponState.Reloading) return;

            if (CurrentWeapon is ModularWeapon simpleWeapon && simpleWeapon.NeedsReload())
            {
                CurrentWeapon.TryReload();
            }
        }

        private void OnPlayerWeaponStateChanged(PlayerWeaponState previous, PlayerWeaponState current)
        {
            // Non-owners synchronize weapon changes from the network state
            if (!IsOwner && previous.weaponIndex != current.weaponIndex)
            {
                SetActiveWeapon(current.weaponIndex);
            }
            ApplyPlayerWeaponState();
        }

        private void ApplyPlayerWeaponState()
        {
            if (m_Animator == null) return;

            // Set animator layer weight based on whether the player has a weapon equipped
            m_Animator.SetLayerWeight(1, m_PlayerWeaponState.Value.hasWeapon ? 1.0f : 0.0f);
        }

        private void UpdatePlayerWeaponState()
        {
            if (!IsSpawned) return;

            var currentState = m_PlayerWeaponState.Value;
            if (currentState.UpdateState(CurrentWeapon != null, m_IsAiming, m_CurrentWeaponIndex))
            {
                m_PlayerWeaponState.Value = currentState;
            }
        }

        private IEnumerator SpawnLoadoutWeaponsCoroutine()
        {
            yield return null;

            foreach (var weaponPrefab in weaponLoadout.weaponPrefabs)
            {
                if (weaponPrefab == null || HasWeaponPrefab(weaponPrefab)) continue;

                GameObject weaponContainer = Instantiate(weaponPrefab);
                NetworkObject weaponNetworkObject = weaponContainer.GetComponent<NetworkObject>();
                if (weaponNetworkObject == null) { Destroy(weaponContainer); continue; }

                AttachableBehaviour attachableWeapon = weaponContainer.GetComponentInChildren<AttachableBehaviour>();
                if (attachableWeapon == null) { Destroy(weaponContainer); continue; }

                ModularWeapon weaponComponent = attachableWeapon.GetComponent<ModularWeapon>();
                if (weaponComponent == null) { Destroy(weaponContainer); continue; }

                weaponNetworkObject.SpawnWithOwnership(OwnerClientId);
                // Register before yielding so a character change cannot orphan a spawned weapon.
                m_SpawnedWeaponAttachables.Add(attachableWeapon);
                m_WeaponImplementations.Add(weaponComponent);
                m_WeaponPrefabNames.Add(weaponPrefab.name.Replace("(Clone)", "").Trim());
                weaponContainer.SetActive(false);
                yield return new WaitUntil(() => weaponNetworkObject == null || weaponNetworkObject.IsSpawned);
            }

            if (m_WeaponImplementations.Count > 0)
            {
                int startIndex = Mathf.Clamp(weaponLoadout.defaultWeaponIndex, 0, m_WeaponImplementations.Count - 1);
                SetActiveWeapon(startIndex);
            }
            m_LoadoutSpawnCoroutine = null;
        }

        private void SwitchToWeapon(int index)
        {
            if (!IsOwner) return;
            SetActiveWeapon(index);
            UpdatePlayerWeaponState();
        }

        private IEnumerator AnimationSyncedWeaponSwitch(int index, IWeapon oldWeapon)
        {
            // TODO: Fix animation timing to match in a better way by using animation events
            // Wait for the weapon switching animation to reach the swap point
            if (oldWeapon != null && oldWeapon != m_WeaponImplementations[index] && m_IsAiming)
            {
                m_Animator.SetBool(k_IsSwitchingWeapon, true);
                yield return new WaitForSeconds(m_WeaponSwitchAnimationSyncTime);
            }

            // Detach the old weapon from the attachment node
            if (m_CurrentAttachableWeapon != null)
            {
                if (m_CurrentAttachableWeapon.HasAuthority) m_CurrentAttachableWeapon.Detach();
            }

            // Update to the new weapon
            m_CurrentWeaponIndex = index;
            m_CurrentAttachableWeapon = m_SpawnedWeaponAttachables[m_CurrentWeaponIndex];
            m_CurrentWeapon = m_WeaponImplementations[m_CurrentWeaponIndex];

            if (m_CurrentAttachableWeapon != null)
            {
                UpdateWeaponAttachment();
            }

            // Raise weapon swap event for owner
            if (IsOwner && onWeaponChanged != null && oldWeapon != m_CurrentWeapon)
            {
                onWeaponChanged.Raise(new WeaponSwapPayload { OldWeapon = oldWeapon, NewWeapon = m_CurrentWeapon });
            }

            // Update animator with new weapon type
            if (m_CurrentWeapon != null)
            {
                var weaponData = m_CurrentWeapon.GetWeaponData();
                if (weaponData != null)
                {
                    m_Animator.SetInteger(m_AnimIDWeaponType, weaponData.weaponTypeID);
                }
            }

            // Update animation rigging overrides for the new weapon
            if (m_CurrentAttachableWeapon != null)
            {
                if (aimController != null && m_CurrentWeapon != null)
                {
                    var weaponData = m_CurrentWeapon.GetWeaponData();
                    if (weaponData != null)
                    {
                        aimController.UpdateAnimationRiggingOverrides(weaponData.spineAimWeight);
                    }
                }
            }

            // Reset weapon switching animator state and coroutine reference
            m_Animator.SetBool(k_IsSwitchingWeapon, false);
            m_WeaponSwitchCoroutine = null;
        }

        private void SetActiveWeapon(int index)
        {
            if (m_WeaponImplementations == null || m_WeaponImplementations.Count == 0)
            {
                m_CurrentWeapon = null;
                m_CurrentAttachableWeapon = null;
                return;
            }

            IWeapon oldWeapon = m_CurrentWeapon;
            oldWeapon?.StopFiring();

            // Reset obstruction state on the old weapon to prevent stuck animator states
            if (oldWeapon is ModularWeapon modularWeapon)
            {
                modularWeapon.ResetObstructionState();
            }

            // Cancel any in-progress weapon switch coroutine
            if (m_WeaponSwitchCoroutine != null)
            {
                StopCoroutine(m_WeaponSwitchCoroutine);
                m_Animator.SetBool(k_IsSwitchingWeapon, false);
            }

            m_WeaponSwitchCoroutine = StartCoroutine(AnimationSyncedWeaponSwitch(index, oldWeapon));
        }

        private void UpdateWeaponAttachment()
        {
            if (m_CurrentAttachableWeapon == null || !m_CurrentAttachableWeapon.HasAuthority || m_CurrentWeapon == null)
            {
                return;
            }

            WeaponData weaponData = m_CurrentWeapon.GetWeaponData();
            if (weaponData == null)
            {
                Debug.LogWarning($"Current weapon '{m_CurrentAttachableWeapon.name}' is missing WeaponData.", this);
                return;
            }

            // Select the appropriate attachment node based on aiming state
            string targetNodeName = (m_IsAiming || CharacterId==0) ? weaponData.handAttachmentNodeName : weaponData.idleAttachmentNodeName;

            if (!string.IsNullOrEmpty(targetNodeName) && m_AttachmentNodes.TryGetValue(targetNodeName, out AttachableNode targetNode))
            {
                if (targetNode != null)
                {
                    m_CurrentAttachableWeapon.Attach(targetNode);
                }
            }
            else if (!string.IsNullOrEmpty(targetNodeName))
            {
                Debug.LogWarning($"AttachableNode named '{targetNodeName}' not found for weapon '{weaponData.weaponName}'.", this);
            }
        }

        #endregion
    }
}


