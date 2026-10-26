using System.Collections;
using System.Collections.Generic;
using Blocks.Gameplay.Core;
using Unity.Netcode;
using UnityEngine;

namespace Blocks.Gameplay.Shooter
{
    public partial class WeaponController
    {
        [Header("Ninja Combat")]
        [SerializeField, Min(0.02f)] private float shurikenBurstInterval = 0.12f;
        [SerializeField, Min(0.4f)] private float shurikenBurstCooldown = 0.65f;
        [SerializeField, Min(1)] private float shurikenDamage = 25f;
        [SerializeField, Min(1)] private float shurikenSpeed = 55f;
        [SerializeField, Min(0.5f)] private float katanaAttackCooldown = 0.65f;
        [SerializeField, Min(0.1f)] private float katanaReach = 2.7f;
        [SerializeField, Min(1)] private float katanaDamage = 100f;
        private float m_NinjaAttackEnds;
        private const float ShurikenThrowDuration = .24f;
        private const float ShurikenReleaseTime = .10f;
        public const int ShurikenMagazineSize = 24;
        private readonly NetworkVariable<int> m_ShurikenAmmo = new NetworkVariable<int>(24, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<bool> m_ShurikenReloading = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private float m_ShurikenReloadEnds;
        private readonly NetworkVariable<double> m_ShurikenReloadStarted = new NetworkVariable<double>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private AudioSource m_ShurikenShotAudio, m_ShurikenReloadAudio;
        private AudioClip m_ShurikenShotClip, m_ShurikenReloadClip;
        private AudioSource m_NinjaAwakeningAudio;
        public int ShurikenAmmo => m_ShurikenAmmo.Value;
        public bool ShurikenReloading => m_ShurikenReloading.Value;

        private void EnsureShurikenAudio()
        {
            if (m_ShurikenShotAudio != null) return;
            m_ShurikenShotClip = Resources.Load<AudioClip>("Audio/Genji/Shot");
            m_ShurikenReloadClip = Resources.Load<AudioClip>("Audio/Genji/Reload");
            var audio = new GameObject("ShurikenAudio");
            audio.transform.SetParent(transform, false);
            m_ShurikenShotAudio = audio.AddComponent<AudioSource>();
            m_ShurikenReloadAudio = audio.AddComponent<AudioSource>();
            m_NinjaAwakeningAudio = audio.AddComponent<AudioSource>();
            foreach (var source in new[] { m_ShurikenShotAudio, m_ShurikenReloadAudio, m_NinjaAwakeningAudio })
            {
                source.playOnAwake = false;
                source.spatialBlend = IsOwner ? 0 : 1;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = 2;
                source.maxDistance = 35;
            }
            m_NinjaAwakeningAudio.clip = Resources.Load<AudioClip>("Audio/Genji/Awakening");
            m_NinjaAwakeningAudio.maxDistance = 60;
            m_NinjaAwakeningAudio.priority = 60;
        }

        private void OnShurikenReloadChanged(bool previous, bool reloading)
        {
            EnsureShurikenAudio();
            m_ShurikenReloadAudio.Stop();
            if (reloading && CharacterId == 1 && m_ShurikenReloadClip != null)
            {
                m_ShurikenReloadAudio.clip = m_ShurikenReloadClip;
                m_ShurikenReloadAudio.Play();
            }
        }

        private void ReloadShurikens()
        {
            if (!CanNinjaAttack || IsKatanaEquipped || ShurikenReloading ||
                ShurikenAmmo == ShurikenMagazineSize || Time.time < m_NinjaAttackEnds) return;
            EnsureShurikenAudio();
            m_ShurikenReloadEnds = Time.time + (m_ShurikenReloadClip != null ? Mathf.Max(.5f, m_ShurikenReloadClip.length) : 1.5f);
            m_ShurikenReloadStarted.Value = NetworkManager.ServerTime.Time;
            m_ShurikenReloading.Value = true;
        }

        private void UpdateShurikenReload()
        {
            if (!IsOwner || !IsSpawned) return;
            if (CharacterId != 1 || (coreStats != null && !coreStats.IsAlive))
            {
                m_ShurikenReloading.Value = false;
                return;
            }
            if (ShurikenReloading && Time.time >= m_ShurikenReloadEnds)
            {
                m_ShurikenAmmo.Value = ShurikenMagazineSize;
                m_ShurikenReloading.Value = false;
            }
            if (ShurikenAmmo == 0) ReloadShurikens();
        }
        private int m_NinjaThrownCount;
        private int m_NextKatanaSwing, m_KatanaSwingStyle;
        private float m_NinjaVisualAttackStart = -100f;

        private bool CanNinjaAttack => IsSpawned && IsOwner && CharacterId == 1 &&
            (coreStats == null || coreStats.IsAlive) && Cursor.lockState == CursorLockMode.Locked &&
            (ShooterEscortMatch.Instance == null || ShooterEscortMatch.Instance.CombatAllowed);

        private void TryNinjaAttack()
        {
            if (ShurikenReloading) return;
            if (!IsKatanaEquipped && ShurikenAmmo <= 0) { ReloadShurikens(); return; }
            if (!CanNinjaAttack || NinjaParrying || (IsKatanaEquipped && UnityEngine.InputSystem.Mouse.current != null && UnityEngine.InputSystem.Mouse.current.rightButton.isPressed) || Time.time < m_NinjaAttackEnds) return;
            m_NinjaAttackEnds = Time.time + (IsKatanaEquipped ? Mathf.Max(KatanaSwingDuration, katanaAttackCooldown) : Mathf.Max(shurikenBurstCooldown, Mathf.Max(shurikenBurstInterval, ShurikenThrowDuration) * 3));
            if (IsKatanaEquipped) StartCoroutine(NinjaSlash());
            else StartCoroutine(NinjaBurst());
        }

        private IEnumerator NinjaBurst()
        {
            for (int i = 0; i < 3; i++)
            {
                if (!CanNinjaAttack || IsKatanaEquipped || ShurikenReloading || ShurikenAmmo <= 0) yield break;
                NinjaThrowPoseRpc(NetworkManager.ServerTime.Time, i);
                yield return new WaitForSeconds(ShurikenReleaseTime);
                if (!CanNinjaAttack || IsKatanaEquipped || ShurikenReloading || ShurikenAmmo <= 0) yield break;
                var view = Camera.main != null ? Camera.main.transform : transform;
                Vector3 target = view.position + view.forward * 100f;
                if (NinjaShurikenFlight.FirstHit(view.position, view.forward, 100f, transform, out var aimHit)) target = aimHit.point;
                // Match the forward wrist release in the authored Throw pose.
                Vector3 origin = view.TransformPoint(shurikenViewPosition + new Vector3(-.06f, .04f, .16f));
                Vector3 offset = origin - view.position;
                if (NinjaShurikenFlight.FirstHit(view.position, offset.normalized, offset.magnitude, transform, out _)) origin = view.position;
                m_ShurikenAmmo.Value--;
                NinjaShurikenRpc(origin, (target - origin).normalized, i);
                if (i < 2) yield return new WaitForSeconds(Mathf.Max(shurikenBurstInterval, ShurikenThrowDuration) - ShurikenReleaseTime);
            }
        }

        [Rpc(SendTo.Everyone)]
        private void NinjaThrowPoseRpc(double started, int burstIndex, RpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId != OwnerClientId || CharacterId != 1) return;
            // Play the full windup on each peer; clock/transport delay must not skip it.
            m_NinjaVisualAttackStart = Time.time;
            m_NinjaThrownCount = Mathf.Clamp(burstIndex, 0, 2);
        }

        [Rpc(SendTo.Everyone)]
        private void NinjaShurikenRpc(Vector3 origin, Vector3 direction, int burstIndex, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId || CharacterId != 1) return;
            // Release is the authoritative visual cue, even if the windup arrived late.
            m_NinjaVisualAttackStart = Time.time - ShurikenReleaseTime;
            EnsureShurikenAudio();
            if (m_ShurikenShotClip != null) m_ShurikenShotAudio.PlayOneShot(m_ShurikenShotClip);
            var prefab = Resources.Load<GameObject>("Ninja/Shuriken");
            if (prefab == null) return;
            m_NinjaThrownCount = Mathf.Clamp(burstIndex + 1, 1, 3);
            var projectile = new GameObject("NinjaThrownShuriken");
            var mesh = Instantiate(prefab, projectile.transform, false);
            NormalizeNinjaModel(mesh.transform, false);
            projectile.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(direction));
            foreach (var collider in projectile.GetComponentsInChildren<Collider>()) collider.enabled = false;
            HeroAttackVisuals.Trail(projectile,new Color(.35f,1,.65f));
            projectile.AddComponent<NinjaShurikenFlight>().Initialize(this, direction.normalized, shurikenSpeed, shurikenDamage, IsOwner);
        }

        private IEnumerator NinjaSlash()
        {
            int style = m_NextKatanaSwing;
            m_NextKatanaSwing = (m_NextKatanaSwing + 1) % 2;
            NinjaSlashVisualRpc(style);
            yield return new WaitForSeconds(KatanaHitTime);
            if (!CanNinjaAttack || !IsKatanaEquipped) yield break;
            var view = Camera.main != null ? Camera.main.transform : transform;
            var origin = view.position;
            var hitTargets = new HashSet<IHittable>();
            foreach (var collider in Physics.OverlapSphere(origin, katanaReach, ~0, QueryTriggerInteraction.Ignore))
            {
                if (collider.transform.IsChildOf(transform)) continue;
                var target = collider.GetComponentInParent<IHittable>();
                if (target == null || hitTargets.Contains(target)) continue;
                var point = collider.ClosestPoint(origin);
                var delta = point - origin;
                if (delta.sqrMagnitude > 0.001f && Vector3.Angle(view.forward, delta) > 65f) continue;
                // A wall or another collider must block the blade.
                if (NinjaShurikenFlight.FirstHit(origin, delta.normalized, delta.magnitude + 0.05f, transform, out var obstacle) &&
                    obstacle.collider.GetComponentInParent<IHittable>() != target) continue;
                hitTargets.Add(target);
                target.OnHit(new HitInfo { amount = katanaDamage, attackerId = OwnerClientId,
                    hitPoint = point, hitNormal = -view.forward, impactForce = view.forward * 35 });
            }
        }

        [Rpc(SendTo.Everyone)]
        private void NinjaSlashVisualRpc(int style, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId || CharacterId != 1) return;
            HeroAttackVisuals.Slash(transform);
            m_KatanaSwingStyle = Mathf.Clamp(style, 0, 2);
            m_NinjaVisualAttackStart = Time.time;
        }
    }

    // Only the firing owner submits damage through the existing IHittable authority path.
    public sealed class NinjaShurikenFlight : MonoBehaviour
    {
        private WeaponController owner;
        private Vector3 direction;
        private float speed, damage, remaining = 100f;
        private bool applyDamage;
        private bool spin = true;
        public void Initialize(WeaponController source, Vector3 heading, float velocity, float amount, bool damageAuthority, float range = 100f, bool rotateInFlight = true)
        {
            remaining = range; spin = rotateInFlight;
            owner = source; direction = heading; speed = velocity; damage = amount; applyDamage = damageAuthority;
            Destroy(gameObject, 4f);
        }
        void Update()
        {
            if (owner == null || !owner.IsSpawned) { Destroy(gameObject); return; }
            float step = Mathf.Min(remaining, speed * Time.deltaTime);
            bool hasHit = FirstHit(transform.position, direction, step, owner.transform, out var hit);
            if (WeaponController.TryInterceptBullet(transform.position, direction, hasHit ? hit.distance : step, owner.OwnerClientId, damage, applyDamage, out _))
            {
                Destroy(gameObject);
                return;
            }
            if (hasHit)
            {
                if (applyDamage)
                    hit.collider.GetComponentInParent<IHittable>()?.OnHit(new HitInfo {
                        amount = damage, attackerId = owner.OwnerClientId, hitPoint = hit.point,
                        hitNormal = hit.normal, impactForce = direction * 15 });
                Destroy(gameObject);
                return;
            }
            transform.position += direction * step;
            if (spin) transform.Rotate(0, 0, 1500 * Time.deltaTime, Space.Self);
            remaining -= step;
            if (remaining <= 0) Destroy(gameObject);
        }
        public static bool FirstHit(Vector3 origin, Vector3 direction, float distance, Transform owner, out RaycastHit nearest)
        {
            nearest = default;
            float best = float.PositiveInfinity;
            foreach (var hit in Physics.RaycastAll(origin, direction, distance, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(owner) || hit.distance >= best) continue;
                best = hit.distance; nearest = hit;
            }
            return best < float.PositiveInfinity;
        }
    }
}
