using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using Blocks.Gameplay.Core;

namespace Blocks.Gameplay.Shooter
{
    public partial class WeaponController
    {
        [Header("Dva Boost / Pilot")]
        [SerializeField, Min(1)] private float dvaBoostSpeed = 10f;
        [SerializeField, Min(1f)] private float dvaBoostStaminaMultiplier = 17f;
        public float DvaBoostEnergy => coreStats == null ? 0 : Mathf.Clamp01(coreStats.GetCurrentValue(StatKeys.Stamina) / Mathf.Max(1f, coreStats.GetMaxValue(StatKeys.Stamina)));
        private float DvaBoostCost => m_DvaMotor == null ? 0 : m_DvaMotor.GetAbilityStaminaCost<WalkAbility>() * dvaBoostStaminaMultiplier;
        [SerializeField, Min(0)] private float dvaBoostCooldown = 5f;
        private readonly NetworkVariable<bool> m_DvaPilot = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<Vector3> m_DvaBombPosition = new NetworkVariable<Vector3>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<Quaternion> m_DvaBombRotation = new NetworkVariable<Quaternion>(Quaternion.identity, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private float m_DvaBoostUntil, m_DvaBoostReady, m_DvaBombFlightUntil;
        private Vector3 m_DvaBombVelocity;
        private CoreMovement m_DvaMotor;
        private Func<Vector3, Vector3> m_DvaPreviousMove;
        private GameObject m_DvaPilotGun;
        private Transform m_DvaPilotMuzzle;
        public const float DvaPilotShotInterval = .14f;
        private float m_DvaPilotShotAt = -100, m_DvaPilotReloadUntil;
        private int m_DvaPilotAmmo = 7;
        private bool m_DvaWasDead;
        private readonly NetworkVariable<double> m_DvaEjectInvulnerableUntil = new NetworkVariable<double>(0,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public bool DvaEjectInvulnerable => IsSpawned && CharacterId == 2 && DvaPilot &&
            coreStats != null && coreStats.IsAlive && NetworkManager.ServerTime.Time < m_DvaEjectInvulnerableUntil.Value;
        public bool DvaPilot => m_DvaPilot.Value;
        public int DvaPilotAmmo => m_DvaPilotAmmo;
        public bool DvaPilotReloading => Time.time < m_DvaPilotReloadUntil;
        public float DvaBoostRemaining => Mathf.Max(0, m_DvaBoostReady - Time.time);
        private bool DvaBoosting => !DvaPilot && CharacterId == 2 && Time.time < m_DvaBoostUntil && coreStats != null && coreStats.IsAlive;

        private void InitializeDvaPilot()
        {
            m_DvaPilotAmmo = 7;
            m_DvaBoostUntil = 0;
            m_DvaBoostReady = 0;
            m_DvaWasDead = false;
            if (IsOwner)
            {
                m_DvaPilot.Value = false;
                m_DvaEjectInvulnerableUntil.Value = 0;
                m_DvaMotor = GetComponent<CoreMovement>();
                if (m_DvaMotor != null)
                {
                    m_DvaPreviousMove = m_DvaMotor.FinalMoveCalculationOverride;
                    m_DvaMotor.FinalMoveCalculationOverride = DvaBoostMovement;
                }
            }
        }
        private Vector3 DvaBoostMovement(Vector3 original)
        {
            if (!DvaBoosting) return m_DvaPreviousMove != null ? m_DvaPreviousMove(original) : original;
            m_DvaMotor.SetVerticalVelocity(0);
            Vector3 direction = Camera.main != null ? Camera.main.transform.forward : transform.forward;
            return direction * dvaBoostSpeed * Time.deltaTime;
        }
        public void RestoreDvaOnRespawn()
        {
            if (!IsOwner || !IsSpawned || CharacterId != 2) return;
            m_DvaWasDead = false;
            m_DvaPilot.Value = false;
                m_DvaEjectInvulnerableUntil.Value = 0;
            m_DvaDetonateAt.Value = 0;
            m_DvaBoostUntil = 0;
            m_DvaBoostReady = 0;
            m_DvaBombFlightUntil = 0;
            m_DvaBombVelocity = Vector3.zero;
            m_DvaPilotAmmo = 7;
            m_DvaPilotReloadUntil = 0;
            m_DvaTrigger = false;
            m_MatrixActive.Value = false;
            m_MatrixEnergy = 1f;
            m_MatrixExhausted = false;
            m_DvaBoostVfxActive.Value = false;
            if (m_DvaMotor != null) m_DvaMotor.UsesExternalSprintStamina = true;
        }
        private void UpdateDvaPilot()
        {
            if (CharacterId != 2) return;
            bool alive = coreStats != null && coreStats.IsAlive;
            if (m_DvaMotor != null) m_DvaMotor.UsesExternalSprintStamina = !DvaPilot && alive;
            if (!alive) { m_DvaWasDead = true; m_DvaBoostUntil = 0; }
            else if (m_DvaWasDead)
            {
                m_DvaWasDead = false;
                m_DvaPilot.Value = false;
                m_DvaEjectInvulnerableUntil.Value = 0;
                m_DvaDetonateAt.Value = 0;
                m_DvaPilotAmmo = 7;
                m_DvaPilotReloadUntil = 0;
                m_DvaBoostUntil = 0;
                m_DvaBoostReady = 0;
                m_DvaBombFlightUntil = 0;
                m_DvaBombVelocity = Vector3.zero;
                m_DvaTrigger = false;
                m_MatrixEnergy = 1f;
                m_MatrixActive.Value = false;
            }
            bool consumingBoost = !DvaPilot && alive && m_DvaBoostUntil > 0;
            if (consumingBoost)
            {
                float stamina = coreStats.GetCurrentValue(StatKeys.Stamina);
                float cost = DvaBoostCost;
                coreStats.TryConsumeStat(StatKeys.Stamina, Mathf.Min(stamina, cost * Time.deltaTime), OwnerClientId);
                stamina = coreStats.GetCurrentValue(StatKeys.Stamina);
                if (stamina <= 0 || cost <= 0)
                {
                    m_DvaBoostUntil = 0;
                    m_DvaBoostReady = Time.time + dvaBoostCooldown;
                }
                else m_DvaBoostUntil = Time.time + stamina / cost;
            }
            if (DvaSelfDestructing && Time.time < m_DvaBombFlightUntil)
            {
                Vector3 step = m_DvaBombVelocity * Time.deltaTime;
                // Sweep a volume so the detached mech stops before walls, including thin walls.
                float distance = step.magnitude;
                float allowed = distance;
                foreach (var hit in Physics.SphereCastAll(m_DvaBombPosition.Value, 0.65f, step.normalized, distance, ~0, QueryTriggerInteraction.Ignore))
                    if (!hit.collider.transform.IsChildOf(transform)) allowed = Mathf.Min(allowed, Mathf.Max(0, hit.distance - 0.03f));
                m_DvaBombPosition.Value += step.normalized * allowed;
                if (allowed < distance) m_DvaBombFlightUntil = 0;
            }
            // Reload timers must finish even while the cursor is unlocked or input is unavailable.
            if (alive && DvaPilot)
            {
                if (m_DvaPilotReloadUntil > 0 && Time.time >= m_DvaPilotReloadUntil)
                { m_DvaPilotAmmo = 7; m_DvaPilotReloadUntil = 0; }
                if (m_DvaPilotAmmo == 0) ReloadDvaPilot();
            }
            if (!alive || Cursor.lockState != CursorLockMode.Locked || Keyboard.current == null) return;
            if (!DvaPilot && (Keyboard.current.leftShiftKey.wasPressedThisFrame || Keyboard.current.rightShiftKey.wasPressedThisFrame))
            {
                if (DvaBoosting) { m_DvaBoostUntil = 0; m_DvaBoostReady = Time.time + dvaBoostCooldown; }
                else if (Time.time >= m_DvaBoostReady && DvaBoostEnergy > 0.01f && DvaBoostCost > 0)
                {
                    // Start identically on the ground or in midair, cancelling downward momentum.
                    if (m_DvaMotor == null) m_DvaMotor = GetComponent<CoreMovement>();
                    if (m_DvaMotor != null)
                    {
                        if (m_DvaMotor.FinalMoveCalculationOverride != DvaBoostMovement)
                        {
                            m_DvaPreviousMove = m_DvaMotor.FinalMoveCalculationOverride;
                            m_DvaMotor.FinalMoveCalculationOverride = DvaBoostMovement;
                        }
                        m_DvaMotor.SetVerticalVelocity(0);
                    }
                    m_DvaBoostUntil = Time.time + coreStats.GetCurrentValue(StatKeys.Stamina) / DvaBoostCost;
                    m_DvaBoostReady = m_DvaBoostUntil + dvaBoostCooldown;
                }
            }
        }
        private void ReloadDvaPilot()
        {
            if (!IsOwner || !IsSpawned || CharacterId != 2 || !DvaPilot || coreStats == null ||
                !coreStats.IsAlive || m_DvaPilotAmmo >= 7 || m_DvaPilotReloadUntil > 0) return;
            m_DvaPilotReloadUntil = Time.time + 1.4f;
        }
        public bool TryEmergencyDvaEject(float incomingDamage)
        {
            if (!IsOwner || !IsSpawned || CharacterId != 2 || DvaPilot || coreStats == null || !coreStats.IsAlive) return false;
            float health = coreStats.GetCurrentValue(StatKeys.Health);
            if (incomingDamage < health) return false;
            // Intercept before Health reaches zero so death/respawn events never fire for mech loss.
            BeginDvaSelfDestruct();
            if (!DvaPilot) return false;
            // A destroyed mech stays at the impact location until its E explosion.
            m_DvaBombFlightUntil = 0;
            m_DvaBombVelocity = Vector3.zero;
            m_DvaBoostVfxActive.Value = false;
            m_DvaPilotAmmo = 7;
            m_DvaPilotReloadUntil = 0;
            coreStats.ModifyStat(StatKeys.Health, coreStats.GetMaxValue(StatKeys.Health) - health, OwnerClientId, ModificationSource.Direct);
            return true;
        }

        private void EjectDva()
        {
            m_DvaBombPosition.Value = transform.position + Vector3.up;
            Vector3 direction = Camera.main != null ? Camera.main.transform.forward : transform.forward;
            m_DvaBombRotation.Value = Quaternion.LookRotation(direction.normalized, Vector3.up);
            m_DvaBombVelocity = DvaBoosting ? direction * dvaBoostSpeed : Vector3.zero;
            m_DvaBombFlightUntil = DvaBoosting ? m_DvaBoostUntil : 0;
            m_DvaBoostUntil = 0;
            m_DvaEjectInvulnerableUntil.Value = NetworkManager.ServerTime.Time + 0.3d;
            m_DvaPilot.Value = true;
            // Leave the detached mech behind instead of putting the pilot inside its cockpit.
            // Move sweeps the player's capsule, so ejection cannot teleport through a wall.
            var controller = GetComponent<CharacterController>();
            if (controller != null && controller.enabled)
                controller.Move(-transform.forward * 1.6f + Vector3.up * .25f);
            if (m_DvaMotor != null) { m_DvaMotor.UsesExternalSprintStamina = false; m_DvaMotor.SetSprintState(false); }
            m_DvaTrigger = false;
            m_MatrixActive.Value = false;
        }
        private void FireDvaPilot()
        {
            if (!IsOwner || !IsSpawned || !DvaPilot || coreStats == null || !coreStats.IsAlive ||
                Cursor.lockState != CursorLockMode.Locked || DvaPilotReloading || m_DvaPilotAmmo <= 0 || Time.time - m_DvaPilotShotAt < DvaPilotShotInterval) return;
            m_DvaPilotAmmo--;
            var view = Camera.main != null ? Camera.main.transform : transform;
            Vector3 origin = view.position;
            Vector3 direction = view.forward;
            bool hit = NinjaShurikenFlight.FirstHit(origin, direction, 60, transform, out var impact);
            Vector3 end = hit ? impact.point : origin + direction * 60;
            if (TryInterceptBullet(origin, direction, hit ? impact.distance : 60, OwnerClientId, 20, true, out var blocked)) end = blocked;
            else if (hit) impact.collider.GetComponentInParent<IHittable>()?.OnHit(new HitInfo {
                amount = 20, attackerId = OwnerClientId, hitPoint = impact.point, hitNormal = impact.normal, impactForce = direction * 3 });
            DvaPilotShotRpc(m_DvaPilotMuzzle != null ? m_DvaPilotMuzzle.position : view.TransformPoint(new Vector3(0.18f, -0.15f, 0.5f)), end);
        }
        [Rpc(SendTo.Everyone)]
        private void DvaPilotShotRpc(Vector3 origin, Vector3 end, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId || CharacterId != 2) return;
            PlayDvaAttackAudio(true);
            m_DvaPilotShotAt = Time.time;
            // Reuse the networked fusion tracer renderer; no extra damage is applied here.
            if(!IsOwner && m_DvaPilotMuzzle!=null)origin=m_DvaPilotMuzzle.position;
            RenderDvaShot(origin, end, true);
        }
        private void DrawDvaPilot()
        {
            bool pilot = DvaPilot;
            bool alive = coreStats != null && coreStats.IsAlive;
            bool bombVisible = DvaSelfDestructing && DvaSelfDestructRemaining > 0;
            foreach (var entry in m_DvaHiddenRenderers)
                if (entry.Key != null) entry.Key.enabled = alive && pilot && m_DvaPilotBody == null && entry.Value;
            if (m_DvaView != null) m_DvaView.SetActive(!pilot && coreStats != null && coreStats.IsAlive);
            if (m_DvaBody != null)
            {
                m_DvaBody.SetActive((alive && !pilot) || bombVisible);
                if (pilot)
                {
                    m_DvaBody.transform.SetPositionAndRotation(m_DvaBombPosition.Value - Vector3.up, m_DvaBombRotation.Value * Quaternion.Euler(0, 90, 0));
                    foreach (var r in m_DvaBody.GetComponentsInChildren<Renderer>()) r.gameObject.layer = gameObject.layer;
                }
                else {
                      float kick = Mathf.Sin(Mathf.Clamp01((Time.time-m_DvaLastShot)/dvaShotInterval)*Mathf.PI);
                      m_DvaBody.transform.localPosition = Vector3.back * (.06f*kick);
                      m_DvaBody.transform.localRotation = Quaternion.Euler(-2.5f*kick, 90, 0);
                  }
            }
            if (pilot && m_DvaPilotGun == null)
            {
                var source = Resources.Load<GameObject>("DvaPilot/PilotPistol");
                if (source == null) return;
                m_DvaPilotGun = new GameObject("Dva_Pilot_LightGun");
                Instantiate(source, m_DvaPilotGun.transform, false);
                m_DvaPilotMuzzle = Array.Find(m_DvaPilotGun.GetComponentsInChildren<Transform>(), t => t.name == "PilotMuzzle");
                // Imported wrist bones can carry FBX unit conversion scale (e.g. 100).
                // Follow the hand in world space without inheriting its scale.
                m_DvaPilotGun.transform.SetParent(transform, false);
                DvaPistolScale.Normalize(m_DvaPilotGun.transform);
                foreach (var c in m_DvaPilotGun.GetComponentsInChildren<Collider>()) c.enabled = false;
            }
            if (m_DvaPilotGun == null) return;
            if (!IsOwner && m_DvaPilotAnimator != null && m_DvaPilotAnimator.isHuman)
            {
                var hand = m_DvaPilotAnimator.GetBoneTransform(HumanBodyBones.RightHand);
                if (hand != null)
                {
                    var middle = m_DvaPilotAnimator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
                    Vector3 palm = middle != null ? Vector3.Lerp(hand.position, middle.position, .65f) : hand.position;
                    float shotKick = Mathf.Sin(Mathf.Clamp01((Time.time-m_DvaPilotShotAt)/DvaPilotShotInterval)*Mathf.PI);
                    m_DvaPilotGun.transform.SetPositionAndRotation(palm-transform.forward*(.025f*shotKick), Quaternion.LookRotation(transform.forward, transform.up)*Quaternion.Euler(-12*shotKick,0,0));
                }
            }
            m_DvaPilotGun.SetActive(pilot && coreStats != null && coreStats.IsAlive);
            if (IsOwner && Camera.main != null)
            {
                float recoil = Mathf.Sin(Mathf.Clamp01((Time.time - m_DvaPilotShotAt) / DvaPilotShotInterval) * Mathf.PI);
                var view = Camera.main.transform;
                m_DvaPilotGun.transform.SetPositionAndRotation(view.TransformPoint(new Vector3(.18f, -.17f, .42f - recoil * .025f)), view.rotation * Quaternion.Euler(-12 * recoil, 0, DvaPilotReloading ? -30 : 0));
            }
        }
        private void CleanupDvaPilot()
        {
            CleanupDvaPilotBody();
            if (m_DvaMotor != null) m_DvaMotor.UsesExternalSprintStamina = false;
            if (m_DvaMotor != null && m_DvaMotor.FinalMoveCalculationOverride == DvaBoostMovement) m_DvaMotor.FinalMoveCalculationOverride = m_DvaPreviousMove;
            if (m_DvaPilotGun != null) Destroy(m_DvaPilotGun);
            m_DvaPilotMuzzle = null;
        }
    }
}
