using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Blocks.Gameplay.Shooter
{
    public partial class WeaponController
    {
        [Header("Ninja / 두 번째 캐릭터")]
        private const float NinjaAwakeningDuration = 5f;
        private const float NinjaAwakeningCooldown = 10f;
        private const float NinjaDrawDuration = .8f;
        private readonly NetworkVariable<double> m_NinjaAwakeningStarted = new NetworkVariable<double>(-100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private double m_NinjaAwakeningReady;
        private float NinjaAwakeningElapsed => NetworkManager != null ? (float)(NetworkManager.ServerTime.Time - m_NinjaAwakeningStarted.Value) : 100;
        public float NinjaAwakeningRemaining => IsKatanaEquipped ? Mathf.Max(0, NinjaAwakeningDuration - NinjaAwakeningElapsed) : 0;
        public float NinjaAwakeningCooldownRemaining => NetworkManager != null ? Mathf.Max(0, (float)(m_NinjaAwakeningReady - NetworkManager.ServerTime.Time)) : 0;

        private void UpdateNinjaAwakening()
        {
            if (!IsOwner || !IsSpawned) return;
            if (m_NinjaKatana.Value && (CharacterId != 1 || (coreStats != null && !coreStats.IsAlive) || NinjaAwakeningRemaining <= 0))
            {
                m_NinjaKatana.Value = false;
                m_Parry.Value = false;
                m_KatanaDashEnds = 0;
                m_NinjaAttackEnds = Time.time + .3f;
                m_ShurikenAmmo.Value = ShurikenMagazineSize;
                m_NinjaAwakeningReady = NetworkManager.ServerTime.Time + NinjaAwakeningCooldown;
            }
            if (m_NinjaDashMotor != null)
            {
                m_NinjaDashMotor.SprintAllowed = CharacterId != 3 && !IsKatanaEquipped;
                if (IsKatanaEquipped) m_NinjaDashMotor.SetSprintState(false);
            }
        }

        private void ActivateNinjaAwakening()
        {
            if (!CanNinjaAttack || IsKatanaEquipped || NinjaAwakeningCooldownRemaining > 0 || Time.time < m_NinjaAttackEnds) return;
            m_ShurikenReloading.Value = false;
            m_NinjaAwakeningStarted.Value = NetworkManager.ServerTime.Time;
            m_NinjaAwakeningReady = NetworkManager.ServerTime.Time + NinjaAwakeningDuration + NinjaAwakeningCooldown;
            m_NinjaKatana.Value = true;
            m_KatanaDashReady = 0;
            m_NextKatanaSwing = 0;
            m_NinjaAttackEnds = Time.time + NinjaDrawDuration;
        }
        [SerializeField] private Vector3 shurikenViewPosition = new Vector3(0.18f, -0.14f, 0.55f);
        [SerializeField] private Vector3 katanaViewPosition = new Vector3(0.28f, -0.26f, 0.65f);
        [SerializeField] private Vector3 shurikenViewRotation = new Vector3(20, 0, 15);
        [SerializeField] private Vector3 katanaViewRotation = new Vector3(8, -10, -22);
        private readonly NetworkVariable<bool> m_NinjaKatana = new NetworkVariable<bool>(false,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private GameObject m_NinjaShurikenVisual, m_NinjaKatanaVisual;
        public bool IsKatanaEquipped => CharacterId == 1 && m_NinjaKatana.Value;

        [Header("Ninja Katana Dash")]
        [SerializeField, Min(1f)] private float katanaDashSpeed = 30f;
        [SerializeField, Min(0.05f)] private float katanaDashDuration = 0.2f;
        private const float KatanaDashCooldown = .1f;
        private const float KatanaDashDamage = 10f;
        private float m_KatanaDashEnds, m_KatanaDashReady;
        private double m_DashSlashStarted = -100;
        private float m_DashSlashDuration;
        private Transform m_DashChest;
        private Quaternion m_DashChestRest;
        private bool m_DashChestAdjusted;
        private const float DashSlashRecovery = .18f;
        [System.Serializable] private class DashSlashKey
        {
            public float time;
            public Vector3 right, left, chest, viewOffset, viewEuler;
        }
        [System.Serializable] private class DashSlashClip
        {
            public float duration;
            public DashSlashKey[] keys;
        }
        private static DashSlashClip s_DashSlashClip;
        private static bool s_DashSlashLoaded;
        private static bool SampleDashSlash(float time, out DashSlashKey pose)
        {
            if (!s_DashSlashLoaded)
            {
                s_DashSlashLoaded = true;
                var asset = Resources.Load<TextAsset>("Ninja/DashSlash");
                if (asset != null) s_DashSlashClip = JsonUtility.FromJson<DashSlashClip>(asset.text);
            }
            return SampleNinjaClip(s_DashSlashClip, time, out pose);
        }

        private static bool SampleNinjaClip(DashSlashClip clip, float time, out DashSlashKey pose)
        {
            pose = null;
            var keys = clip?.keys;
            if (keys == null || keys.Length < 2) return false;
            int index = 1;
            while (index < keys.Length - 1 && time > keys[index].time) index++;
            var a = keys[index - 1]; var b = keys[index];
            float t = Mathf.InverseLerp(a.time, b.time, time);
            pose = new DashSlashKey {
                right = Vector3.Lerp(a.right, b.right, t), left = Vector3.Lerp(a.left, b.left, t),
                chest = Vector3.Lerp(a.chest, b.chest, t), viewOffset = Vector3.Lerp(a.viewOffset, b.viewOffset, t),
                viewEuler = Vector3.Lerp(a.viewEuler, b.viewEuler, t)
            };
            return true;
        }
        private float DashSlashElapsed => NetworkManager != null ? (float)(NetworkManager.ServerTime.Time - m_DashSlashStarted) : 100f;
        private static readonly System.Collections.Generic.Dictionary<string, DashSlashClip> s_NinjaActions = new System.Collections.Generic.Dictionary<string, DashSlashClip>();
        private bool ApplyNinjaActionPose(ref Vector3 position, ref Quaternion rotation, float attackElapsed)
        {
            string action = null;
            float phase = 0;
            if (coreStats != null && !coreStats.IsAlive) return false;
            if (IsKatanaEquipped && NinjaAwakeningElapsed < NinjaDrawDuration)
            { action = "Draw"; phase = NinjaAwakeningElapsed / NinjaDrawDuration; }
            else if (!IsKatanaEquipped && ShurikenReloading)
            {
                EnsureShurikenAudio();
                float duration = m_ShurikenReloadClip != null ? Mathf.Max(.5f, m_ShurikenReloadClip.length) : 1.5f;
                action = "Reload"; phase = (float)(NetworkManager.ServerTime.Time - m_ShurikenReloadStarted.Value) / duration;
            }
            else if (IsKatanaEquipped && !NinjaParrying && attackElapsed >= 0 && attackElapsed < KatanaSwingDuration)
            { action = m_KatanaSwingStyle % 2 == 0 ? "SlashRight" : "SlashLeft"; phase = attackElapsed / KatanaSwingDuration; }
            else if (!IsKatanaEquipped && attackElapsed >= 0 && attackElapsed < ShurikenThrowDuration)
            { action = "Throw"; phase = attackElapsed / ShurikenThrowDuration; }
            if (action == null) return false;
            if (!s_NinjaActions.TryGetValue(action, out var clip))
            {
                var asset = Resources.Load<TextAsset>("Ninja/" + action);
                clip = asset != null ? JsonUtility.FromJson<DashSlashClip>(asset.text) : null;
                s_NinjaActions[action] = clip;
            }
            if (!SampleNinjaClip(clip, Mathf.Clamp01(phase), out var pose)) return false;
            position = (IsKatanaEquipped ? katanaViewPosition : shurikenViewPosition) + pose.viewOffset;
            rotation = Quaternion.Euler(pose.viewEuler);
            if (m_Animator == null || !m_Animator.isHuman) return true;
            m_DashChest = m_Animator.GetBoneTransform(HumanBodyBones.Chest);
            if (m_DashChest != null)
            {
                m_DashChestRest = m_DashChest.localRotation; m_DashChestAdjusted = true;
                m_DashChest.localRotation *= Quaternion.Euler(pose.chest);
            }
            SolveArm(m_Animator, transform, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, pose.right, 1);
            SolveArm(m_Animator, transform, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, pose.left, -1);
            var hand = m_Animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (hand != null) hand.rotation = transform.rotation * rotation * Quaternion.Euler(0,0,-90);
            if (IsKatanaEquipped) ApplyKatanaTwoHandGrip(rotation);
            return true;
        }

        private void ApplyKatanaTwoHandGrip(Quaternion bladeRotation)
        {
            if (m_Animator == null || !m_Animator.isHuman) return;
            var right = m_Animator.GetBoneTransform(HumanBodyBones.RightHand);
            var left = m_Animator.GetBoneTransform(HumanBodyBones.LeftHand);
            var upper = m_Animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            var lower = m_Animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            if (right == null || left == null || upper == null || lower == null) return;
            Quaternion worldRotation = transform.rotation * bladeRotation;
            Vector3 gripOffset = worldRotation * new Vector3(0, -.14f, 0);
            float reach = Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, left.position) - .015f;
            // Keep both grips reachable instead of stretching the support arm.
            for (int i = 0; i < 3; i++)
            {
                Vector3 delta = right.position + gripOffset - upper.position;
                if (delta.magnitude <= reach) break;
                Vector3 target = right.position - delta.normalized * (delta.magnitude - reach);
                SolveArm(m_Animator, transform, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm,
                    HumanBodyBones.RightHand, transform.InverseTransformPoint(target), 1);
            }
            right.rotation = worldRotation * Quaternion.Euler(0, 0, -90);
            SolveArm(m_Animator, transform, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm,
                HumanBodyBones.LeftHand, transform.InverseTransformPoint(right.position + gripOffset), -1);
            left.rotation = worldRotation * Quaternion.Euler(0, 180, 90);
        }
        private bool DashSlashVisible => IsKatanaEquipped && coreStats != null && coreStats.IsAlive &&
            DashSlashElapsed >= 0 && DashSlashElapsed < m_DashSlashDuration + DashSlashRecovery;
        private Vector3 m_KatanaDashDirection;
        private Blocks.Gameplay.Core.CoreMovement m_NinjaDashMotor;
        private System.Func<Vector3, Vector3> m_NinjaPreviousMove;
        public float KatanaDashCooldownRemaining => Mathf.Max(0, m_KatanaDashReady - Time.time);
        private bool KatanaDashing => IsSpawned && IsOwner && IsKatanaEquipped &&
            coreStats != null && coreStats.IsAlive && Time.time < m_KatanaDashEnds;

        private Vector3 KatanaDashMovement(Vector3 original)
        {
            if (CassidyRollHasMovement) return CassidyRollMovement(original);
            if (!KatanaDashing) return m_NinjaPreviousMove != null ? m_NinjaPreviousMove(original) : original;
            m_NinjaDashMotor.SetVerticalVelocity(0);
            // CoreMovement applies this through CharacterController.Move, preserving wall collision.
            return m_KatanaDashDirection * katanaDashSpeed * Time.deltaTime;
        }
        private void UpdateKatanaDash()
        {
            if (!IsKatanaEquipped || coreStats == null || !coreStats.IsAlive || Cursor.lockState != CursorLockMode.Locked)
            { m_KatanaDashEnds = 0; return; }
            if (Keyboard.current == null || !Keyboard.current.leftShiftKey.wasPressedThisFrame ||
                Time.time < m_KatanaDashReady || Time.time < m_NinjaAttackEnds || m_NinjaDashMotor == null) return;
            m_KatanaDashDirection = Camera.main != null ? Camera.main.transform.forward : transform.forward;
            m_KatanaDashEnds = Time.time + katanaDashDuration;
            m_KatanaDashReady = Time.time + KatanaDashCooldown;
            m_NinjaDashMotor.SetSprintState(false);
            m_NinjaDashMotor.SetVerticalVelocity(0);
            m_Parry.Value = false;
            m_NinjaAttackEnds = Time.time + Mathf.Min(katanaDashDuration + DashSlashRecovery, KatanaDashCooldown);
            NinjaDashSlashRpc(NetworkManager.ServerTime.Time, katanaDashDuration);
            StartCoroutine(NinjaDashSlashDamage());
        }

        [Rpc(SendTo.Everyone)]
        private void NinjaDashSlashRpc(double started, float duration, RpcParams rpc = default)
        {
            if(rpc.Receive.SenderClientId != OwnerClientId || CharacterId != 1)return;
            m_DashSlashStarted=started;
            m_DashSlashDuration=Mathf.Clamp(duration,.05f,2f);
            m_NinjaVisualAttackStart=-100f;
            m_ParryBlend=0;
        }

        private System.Collections.IEnumerator NinjaDashSlashDamage()
        {
            var targets=new System.Collections.Generic.HashSet<Blocks.Gameplay.Core.IHittable>();
            Vector3 previous=transform.position+Vector3.up;
            while(KatanaDashing)
            {
                // Sweep the distance actually travelled, after CharacterController wall collision.
                yield return new WaitForEndOfFrame();
                if(!CanNinjaAttack || !IsKatanaEquipped || !isActiveAndEnabled ||
                    (ShooterEscortMatch.Instance!=null && !ShooterEscortMatch.Instance.CombatAllowed))yield break;
                Vector3 current=transform.position+Vector3.up;
                foreach(var collider in Physics.OverlapCapsule(previous,current,.75f,~0,QueryTriggerInteraction.Ignore))
                {
                    if(collider.transform.IsChildOf(transform))continue;
                    var target=collider.GetComponentInParent<Blocks.Gameplay.Core.IHittable>();
                    if(target==null || targets.Contains(target))continue;
                    Vector3 travel=current-previous;
                    Vector3 origin=previous+travel*(travel.sqrMagnitude>.0001f ? Mathf.Clamp01(Vector3.Dot(collider.bounds.center-previous,travel)/travel.sqrMagnitude):0);
                    Vector3 point=collider.ClosestPoint(origin), delta=point-origin;
                    if(delta.sqrMagnitude>.0001f && NinjaShurikenFlight.FirstHit(origin,delta.normalized,delta.magnitude+.05f,transform,out var obstacle) &&
                        obstacle.collider.GetComponentInParent<Blocks.Gameplay.Core.IHittable>()!=target)continue;
                    targets.Add(target);
                    target.OnHit(new Blocks.Gameplay.Core.HitInfo{amount=KatanaDashDamage,attackerId=OwnerClientId,
                        hitPoint=point,hitNormal=-m_KatanaDashDirection,impactForce=m_KatanaDashDirection*35});
                }
                previous=current;
            }
        }

        private void RestoreNinjaDashBody()
        {
            if(m_DashChestAdjusted && m_DashChest!=null)m_DashChest.localRotation=m_DashChestRest;
            m_DashChestAdjusted=false;
        }

        private void ApplyNinjaDashSlash(ref Vector3 position, ref Quaternion rotation)
        {
            float elapsed=DashSlashElapsed;
            // Fit one horizontal cut inside the existing dash, then recover without stopping movement.
            float cutTime=elapsed<m_DashSlashDuration
                ? elapsed/m_DashSlashDuration*(KatanaWindupDuration+KatanaCutDuration)
                : KatanaWindupDuration+KatanaCutDuration+(elapsed-m_DashSlashDuration)/DashSlashRecovery*(KatanaSwingDuration-KatanaWindupDuration-KatanaCutDuration);
            EvaluateKatanaSwing(cutTime,1,ref position,ref rotation);
            // Blender's authored 0.20 s cut + 0.18 s recovery follows Inspector dash timing.
            float authoredTime = elapsed < m_DashSlashDuration
                ? elapsed / m_DashSlashDuration * .20f : .20f + elapsed - m_DashSlashDuration;
            bool hasPose = SampleDashSlash(authoredTime, out var pose);
            if (hasPose)
            {
                position = katanaViewPosition + pose.viewOffset;
                rotation = Quaternion.Euler(pose.viewEuler);
            }
            if(m_Animator==null || !m_Animator.isHuman)return;
            float cut=Mathf.SmoothStep(0,1,Mathf.InverseLerp(m_DashSlashDuration*.25f,m_DashSlashDuration,elapsed));
            float recover=Mathf.Clamp01((elapsed-m_DashSlashDuration)/DashSlashRecovery);
            float weight=1-Mathf.SmoothStep(0,1,recover);
            m_DashChest=m_Animator.GetBoneTransform(HumanBodyBones.Chest);
            if(m_DashChest!=null){m_DashChestRest=m_DashChest.localRotation;m_DashChestAdjusted=true;m_DashChest.localRotation*=Quaternion.Euler(hasPose ? pose.chest : new Vector3(18*weight,Mathf.Lerp(15,-25,cut)*weight,0));}
            var right=m_Animator.GetBoneTransform(HumanBodyBones.RightHand);
            if(right==null)return;
            Vector3 target=hasPose ? pose.right : Vector3.Lerp(new Vector3(.38f,1.25f,.12f),new Vector3(-.30f,1.15f,.45f),cut);
            target=Vector3.Lerp(target,transform.InverseTransformPoint(right.position),1-weight);
            SolveArm(m_Animator,transform,HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm,HumanBodyBones.RightHand,target,1);
            right.rotation=Quaternion.Slerp(right.rotation,transform.rotation*rotation*Quaternion.Euler(0,0,-90),weight);
            var left = m_Animator.GetBoneTransform(HumanBodyBones.LeftHand);
            if (hasPose && left != null)
            {
                Vector3 leftTarget = Vector3.Lerp(transform.InverseTransformPoint(left.position), pose.left, weight);
                SolveArm(m_Animator, transform, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm,
                    HumanBodyBones.LeftHand, leftTarget, -1);
            }
        }

        private void InitializeNinjaWeapons()
        {
            InitializeGenjiLocomotion();
            if (IsOwner) { m_ShurikenAmmo.Value = ShurikenMagazineSize; m_ShurikenReloading.Value = false; }
            m_ShurikenReloading.OnValueChanged += OnShurikenReloadChanged;
            m_ParryBlend = 0; m_ParryImpact = -100f;
            if (IsOwner) m_Parry.Value = false;
            m_KatanaDashEnds = m_KatanaDashReady = 0;
            m_DashSlashStarted=-100;
            if (IsOwner)
            {
                m_NinjaDashMotor = GetComponent<Blocks.Gameplay.Core.CoreMovement>();
                if (m_NinjaDashMotor != null)
                {
                    m_NinjaPreviousMove = m_NinjaDashMotor.FinalMoveCalculationOverride;
                    m_NinjaDashMotor.FinalMoveCalculationOverride = KatanaDashMovement;
                }
            }
            m_NextKatanaSwing = 0;
            m_KatanaSwingStyle = 0;
            ConfigureNinjaJump();
            if (IsOwner) m_NinjaKatana.Value = false;
            m_CharacterId.OnValueChanged += OnNinjaCharacterChanged;
            m_NinjaKatana.OnValueChanged += OnNinjaWeaponChanged;
            RefreshNinjaWeapons();
        }

        private void ConfigureNinjaJump()
        {
            var jump = GetComponent<Blocks.Gameplay.Core.JumpAbility>();
            if (jump != null) jump.SetAllowedAirJumps(CharacterId == 1 ? 1 : 0);
        }

        private void OnNinjaCharacterChanged(int previous, int current)
        {
            if (current != 1 && m_NinjaAwakeningAudio != null) m_NinjaAwakeningAudio.Stop();
            ConfigureNinjaJump();
            RefreshNinjaWeapons();
        }
        private void OnNinjaWeaponChanged(bool previous, bool current)
        {
            if (current && !previous && CharacterId == 1)
            {
                EnsureShurikenAudio();
                if (m_NinjaAwakeningAudio.clip != null) m_NinjaAwakeningAudio.Play();
            }
            else if (!current && m_NinjaAwakeningAudio != null) m_NinjaAwakeningAudio.Stop();
            m_NinjaVisualAttackStart = -100f;
            RefreshNinjaWeapons();
        }

        private void UpdateNinjaWeaponInput()
        {
            UpdateShurikenReload();
            UpdateKatanaDash();
            UpdateNinjaParry();
            if (!IsSpawned || !IsOwner || CharacterId != 1 || Keyboard.current == null) return;
            if (coreStats != null && !coreStats.IsAlive) return;
            if (Cursor.lockState != CursorLockMode.Locked) return;
            if (Time.time < m_NinjaAttackEnds) return;
            if (Keyboard.current.eKey.wasPressedThisFrame) ActivateNinjaAwakening();
        }

        private void RefreshNinjaWeapons()
        {
            if (CharacterId == 1)
            {
                if (m_NinjaShurikenVisual == null) m_NinjaShurikenVisual = CreateNinjaVisual("Shuriken", false);
                if (m_NinjaKatanaVisual == null) m_NinjaKatanaVisual = CreateNinjaVisual("Katana", true);
            }
            if (m_NinjaShurikenVisual != null) m_NinjaShurikenVisual.SetActive(CharacterId == 1 && !m_NinjaKatana.Value);
            if (m_NinjaKatanaVisual != null) m_NinjaKatanaVisual.SetActive(CharacterId == 1 && m_NinjaKatana.Value);
        }

        private GameObject CreateNinjaVisual(string resourceName, bool katana)
        {
            var prefab = Resources.Load<GameObject>("Ninja/" + resourceName);
            if (prefab == null)
            {
                Debug.LogError("Ninja weapon prefab missing: Resources/Ninja/" + resourceName, this);
                return null;
            }
            Transform mount = null;
            if (IsOwner)
            {
                foreach (var child in GetComponentsInChildren<Transform>(true))
                    if (child.name == "PlayerCameraRoot") { mount = child; break; }
            }
            else if (m_Animator != null && m_Animator.isHuman)
                mount = m_Animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (mount == null) mount = transform;
            var visual = new GameObject("Ninja_" + resourceName);
            visual.transform.SetParent(mount, false);
            var mountScale = mount.lossyScale;
            visual.transform.localScale = new Vector3(1 / Mathf.Max(.0001f, Mathf.Abs(mountScale.x)),
                1 / Mathf.Max(.0001f, Mathf.Abs(mountScale.y)), 1 / Mathf.Max(.0001f, Mathf.Abs(mountScale.z)));
            int count = katana ? 1 : 3;
            for (int i = 0; i < count; i++)
            {
                var model = Instantiate(prefab, visual.transform, false);
                NormalizeNinjaModel(model.transform, katana);
                if (!katana)
                {
                    model.transform.localPosition = new Vector3((i - 1) * 0.042f, 0, Mathf.Abs(i - 1) * 0.012f);
                    model.transform.localRotation = Quaternion.Euler(0, 0, (i - 1) * 24) * model.transform.localRotation;
                }
                foreach (var collider in model.GetComponentsInChildren<Collider>()) collider.enabled = false;
            }
            visual.name = "Ninja_" + resourceName;
            visual.transform.localPosition = IsOwner ? (katana ? katanaViewPosition : shurikenViewPosition) : Vector3.zero;
            visual.transform.localRotation = Quaternion.Euler(IsOwner
                ? (katana ? katanaViewRotation : shurikenViewRotation) : new Vector3(0, 0, 90));
            return visual;
        }

        private static void NormalizeNinjaModel(Transform model, bool katana)
        {
            // FBX forward conventions vary. Read geometry in model space rather than guessing 180-degree offsets.
            bool initialized = false;
            Bounds bounds = default;
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null) continue;
                var meshBounds = filter.sharedMesh.bounds;
                var matrix = model.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                for (int corner = 0; corner < 8; corner++)
                {
                    var p = meshBounds.center + Vector3.Scale(meshBounds.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    p = matrix.MultiplyPoint3x4(p);
                    if (!initialized) { bounds = new Bounds(p, Vector3.zero); initialized = true; }
                    else bounds.Encapsulate(p);
                }
            }
            if (!initialized) return;
            Vector3 size = bounds.size;
            int axis = katana ? (size.x > size.y && size.x > size.z ? 0 : size.y > size.z ? 1 : 2)
                : (size.x < size.y && size.x < size.z ? 0 : size.y < size.z ? 1 : 2);
            Vector3 direction = Vector3.zero;
            direction[axis] = katana && Mathf.Abs(bounds.min[axis]) > Mathf.Abs(bounds.max[axis]) ? -1 : 1;
            model.localRotation = Quaternion.FromToRotation(direction, katana ? Vector3.up : Vector3.forward);
            // Measure after parenting and rotation: camera/wrist import scales can be 100x.
            Bounds world = default;
            bool first = true;
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null) continue;
                var b = filter.sharedMesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    var p = filter.transform.TransformPoint(b.center + Vector3.Scale(b.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)));
                    if (first) { world = new Bounds(p, Vector3.zero); first = false; } else world.Encapsulate(p);
                }
            }
            model.localScale *= (katana ? 1.08f : .105f) / Mathf.Max(world.size.x, world.size.y, world.size.z, .001f);
        }

        private const float KatanaWindupDuration = 0.16f;
        private const float KatanaCutDuration = 0.16f;
        private const float KatanaSwingDuration = 0.65f;
        private const float KatanaHitTime = KatanaWindupDuration + KatanaCutDuration * 0.5f;

        private static void EvaluateKatanaSwing(float elapsed, int style, ref Vector3 position, ref Quaternion rotation)
        {
            Vector3 ready, finish;
            Quaternion readyRotation, finishRotation;
            // Blade rests along local up. Each cut crosses the aim line at its midpoint.
            switch (style)
            {
                case 0: // Vertical: raise the blade, then cut down in front of the camera.
                    ready = new Vector3(-0.20f, 0.20f, -0.08f);
                    finish = new Vector3(-0.20f, -0.16f, 0.16f);
                    readyRotation = Quaternion.Euler(-45, 0, 15);
                    finishRotation = Quaternion.Euler(120, 0, 15);
                    break;
                case 1: // Horizontal: right to left.
                    ready = new Vector3(0.12f, 0.12f, -0.06f);
                    finish = new Vector3(-0.52f, 0.12f, 0.12f);
                    readyRotation = Quaternion.Euler(65, 65, -70);
                    finishRotation = Quaternion.Euler(65, -70, -70);
                    break;
                default: // Diagonal: upper right to lower left.
                    ready = new Vector3(0.09f, 0.22f, -0.06f);
                    finish = new Vector3(-0.46f, -0.15f, 0.14f);
                    readyRotation = Quaternion.Euler(-25, 15, -50);
                    finishRotation = Quaternion.Euler(85, -25, 100);
                    break;
            }
            Vector3 offset;
            Quaternion turn;
            if (elapsed < KatanaWindupDuration)
            {
                float t = Mathf.SmoothStep(0, 1, elapsed / KatanaWindupDuration);
                offset = Vector3.Lerp(Vector3.zero, ready, t);
                turn = Quaternion.Slerp(Quaternion.identity, readyRotation, t);
            }
            else if (elapsed < KatanaWindupDuration + KatanaCutDuration)
            {
                float t = Mathf.SmoothStep(0, 1, (elapsed - KatanaWindupDuration) / KatanaCutDuration);
                offset = Vector3.Lerp(ready, finish, t);
                turn = Quaternion.Slerp(readyRotation, finishRotation, t);
            }
            else
            {
                float t = Mathf.SmoothStep(0, 1, (elapsed - KatanaWindupDuration - KatanaCutDuration) /
                    (KatanaSwingDuration - KatanaWindupDuration - KatanaCutDuration));
                offset = Vector3.Lerp(finish, Vector3.zero, t);
                turn = Quaternion.Slerp(finishRotation, Quaternion.identity, t);
            }
            position += offset;
            rotation *= turn;
        }

        private void LateUpdate()
        {
            UpdateSoldierBody();
            FitSoldierShoulders();
            ApplySoldierHoldPose();
            PositionSoldierView();
            UpdateDvaPilotBody();
            LateUpdateDva();
            ApplyRevolverBodyMotion();
            ApplyCassidyRollPose();
            UpdateCowboyBody();
            LateUpdateRevolver();
            if (CharacterId != 1) UpdateGenjiHeroBody();
            if (!IsSpawned || CharacterId != 1) return;
            if (m_NinjaShurikenVisual != null)
            {
                int hidden = Time.time - m_NinjaVisualAttackStart < 0.28f ? m_NinjaThrownCount : 0;
                if (ShurikenReloading)
                {
                    EnsureShurikenAudio();
                    float duration = m_ShurikenReloadClip != null ? Mathf.Max(.5f, m_ShurikenReloadClip.length) : 1.5f;
                    float progress = (float)(NetworkManager.ServerTime.Time - m_ShurikenReloadStarted.Value) / duration;
                    if (progress > .2f && progress < .7f) hidden = 3;
                }
                for (int i = 0; i < m_NinjaShurikenVisual.transform.childCount; i++)
                    m_NinjaShurikenVisual.transform.GetChild(i).gameObject.SetActive(i >= hidden);
            }
            var visual = m_NinjaKatana.Value ? m_NinjaKatanaVisual : m_NinjaShurikenVisual;
            Vector3 position = m_NinjaKatana.Value ? katanaViewPosition : shurikenViewPosition;
            Quaternion rotation = Quaternion.Euler(m_NinjaKatana.Value ? katanaViewRotation : shurikenViewRotation);
            float elapsed = Time.time - m_NinjaVisualAttackStart;
            if (elapsed >= 0 && elapsed < (IsKatanaEquipped ? KatanaSwingDuration : .5f))
            {
                
                if (m_NinjaKatana.Value)
                {
                    EvaluateKatanaSwing(elapsed, m_KatanaSwingStyle, ref position, ref rotation);
                }
                else
                {
                    float flick = Mathf.Sin(Mathf.Clamp01(elapsed / 0.12f) * Mathf.PI);
                    position += new Vector3(-0.035f, 0.025f, 0.12f) * flick;
                    rotation *= Quaternion.Euler(-25 * flick, 10 * flick, -18 * flick);
                }
            }
            bool dashSlash=DashSlashVisible;
            if(dashSlash) ApplyNinjaDashSlash(ref position,ref rotation);
            else if (m_NinjaKatana.Value) ApplyParryPose(ref position, ref rotation);
            bool actionPose = !dashSlash && ApplyNinjaActionPose(ref position, ref rotation, elapsed);
            if (IsKatanaEquipped && !actionPose)
            {
                ApplyKatanaTwoHandGrip(rotation);
                actionPose = true;
            }
            UpdateGenjiHeroBody();
            if (visual == null) return;
            if (IsOwner && Camera.main != null)
            {
                var cameraTransform = Camera.main.transform;
                visual.transform.SetPositionAndRotation(cameraTransform.TransformPoint(position), cameraTransform.rotation * rotation);
            }
            else
            {
                if(dashSlash || actionPose){visual.transform.localRotation=Quaternion.Euler(0,0,90);return;}
                Quaternion handRotation = Quaternion.Euler(0, 0, 90);
                if (m_NinjaKatana.Value && elapsed >= 0 && elapsed < KatanaSwingDuration)
                {
                    Vector3 unused = Vector3.zero;
                    Quaternion swingRotation = Quaternion.identity;
                    EvaluateKatanaSwing(elapsed, m_KatanaSwingStyle, ref unused, ref swingRotation);
                    handRotation *= swingRotation;
                }
                handRotation = Quaternion.Slerp(handRotation, Quaternion.Euler(-10, -15, 148 + 12 * Mathf.Sin(Mathf.Clamp01((Time.time - m_ParryImpact) / 0.16f) * Mathf.PI)), m_ParryBlend);
                visual.transform.localRotation = handRotation;
            }
        }

        private void CleanupNinjaWeapons()
        {
            CleanupGenjiHeroBody();
            m_ShurikenReloading.OnValueChanged -= OnShurikenReloadChanged;
            if (m_ShurikenShotAudio != null) Destroy(m_ShurikenShotAudio.gameObject);
            RestoreNinjaDashBody();
            m_DashSlashStarted=-100;
            m_KatanaDashEnds = 0;
            if (m_NinjaDashMotor != null && m_NinjaDashMotor.FinalMoveCalculationOverride == KatanaDashMovement)
                m_NinjaDashMotor.FinalMoveCalculationOverride = m_NinjaPreviousMove;
            StopAllCoroutines();
            m_NinjaAttackEnds = 0;
            var jump = GetComponent<Blocks.Gameplay.Core.JumpAbility>();
            if (jump != null) jump.SetAllowedAirJumps(0);
            m_CharacterId.OnValueChanged -= OnNinjaCharacterChanged;
            m_NinjaKatana.OnValueChanged -= OnNinjaWeaponChanged;
            if (m_NinjaShurikenVisual != null) Destroy(m_NinjaShurikenVisual);
            if (m_NinjaKatanaVisual != null) Destroy(m_NinjaKatanaVisual);
        }
    }
}

