using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using Blocks.Gameplay.Core;
namespace Blocks.Gameplay.Shooter
{
    public partial class WeaponController
    {
        [Header("Ninja Parry")]
        [SerializeField, Range(10, 89)] private float parryHalfAngle = 65;
        private readonly NetworkVariable<bool> m_Parry = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<Vector3> m_ParryOrigin = new NetworkVariable<Vector3>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<Quaternion> m_ParryRotation = new NetworkVariable<Quaternion>(Quaternion.identity, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private float m_ParryBlend, m_ParryImpact = -100;
        public bool NinjaParrying => IsSpawned && isActiveAndEnabled && IsKatanaEquipped && coreStats != null && coreStats.IsAlive && m_Parry.Value;
        private void UpdateNinjaParry()
        {
            if (!IsSpawned || !IsOwner) return;
            bool active = IsKatanaEquipped && coreStats != null && coreStats.IsAlive && Cursor.lockState == CursorLockMode.Locked &&
                Mouse.current != null && Mouse.current.rightButton.isPressed && Time.time >= m_NinjaAttackEnds;
            if (active)
            {
                var view = Camera.main != null ? Camera.main.transform : transform;
                m_ParryOrigin.Value = view.position;
                m_ParryRotation.Value = view.rotation;
            }
            m_Parry.Value = active;
        }
        // Only the original firing authority resolves damage, including reflections.
        public static bool TryInterceptBullet(Vector3 origin, Vector3 direction, float distance, ulong attacker,
            float damage, bool resolve, out Vector3 point, int bounce = 0)
        {
            bool blocked = TryBlockDvaMatrix(origin, direction, distance, attacker, out point);
            float nearest = blocked ? Vector3.Distance(origin, point) : distance;
            WeaponController defender = null;
            direction.Normalize();
            foreach (var candidate in s_MatrixPlayers)
            {
                if (candidate == null || !candidate.NinjaParrying || candidate.OwnerClientId == attacker) continue;
                var inverse = Quaternion.Inverse(candidate.m_ParryRotation.Value);
                var localDirection = inverse * direction;
                if (-localDirection.z < Mathf.Cos(candidate.parryHalfAngle * Mathf.Deg2Rad)) continue;
                var localOrigin = inverse * (origin - candidate.m_ParryOrigin.Value);
                var bounds = new Bounds(new Vector3(0, -0.35f, 0.65f), new Vector3(1.5f, 2.1f, 0.25f));
                float entry = 0;
                if (!bounds.Contains(localOrigin) && !bounds.IntersectRay(new Ray(localOrigin, localDirection), out entry)) continue;
                if (entry < 0 || entry > nearest) continue;
                nearest = entry; point = origin + direction * entry; defender = candidate; blocked = true;
            }
            if (defender == null || !resolve) return blocked;
            Vector3 heading = defender.m_ParryRotation.Value * Vector3.forward;
            Vector3 aim = defender.m_ParryOrigin.Value + heading * 100;
            if (NinjaShurikenFlight.FirstHit(defender.m_ParryOrigin.Value, heading, 100, defender.transform, out var aimHit)) aim = aimHit.point;
            heading = (aim - point).normalized;
            Vector3 start = point + heading * 0.03f;
            bool hit = NinjaShurikenFlight.FirstHit(start, heading, 100, defender.transform, out var impact);
            Vector3 end = hit ? impact.point : start + heading * 100;
            // Bounded recursion when two players reflect into each other.
            if (bounce >= 4) end = start;
            else if (TryInterceptBullet(start, heading, hit ? impact.distance : 100, defender.OwnerClientId, damage, true, out var next, bounce + 1)) end = next;
            else if (hit) impact.collider.GetComponentInParent<IHittable>()?.OnHit(new HitInfo {
                amount = damage, attackerId = defender.OwnerClientId, hitPoint = impact.point,
                hitNormal = impact.normal, impactForce = heading * 15 });
            defender.NinjaParryVisualRpc(point, end);
            return true;
        }
        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Everyone)]
        private void NinjaParryVisualRpc(Vector3 point, Vector3 end)
        {
            if (!IsKatanaEquipped) return;
            if (Time.time - m_ParryImpact >= 0.08f) m_ParryImpact = Time.time;
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) return;
            var material = new Material(shader);
            material.color = new Color(0.35f, 1, 0.9f);
            var effect = new GameObject("Katana reflected bullet");
            var line = effect.AddComponent<LineRenderer>();
            line.sharedMaterial = material; line.startWidth = 0.035f; line.endWidth = 0.008f;
            line.positionCount = 2; line.SetPosition(0, point); line.SetPosition(1, end);
            Destroy(effect, 0.09f); Destroy(material, 0.12f);
        }
        private void ApplyParryPose(ref Vector3 position, ref Quaternion rotation)
        {
            m_ParryBlend = Mathf.MoveTowards(m_ParryBlend, NinjaParrying ? 1 : 0, Time.deltaTime / 0.12f);
            float kick = Mathf.Sin(Mathf.Clamp01((Time.time - m_ParryImpact) / 0.16f) * Mathf.PI);
            position = Vector3.Lerp(position, new Vector3(0.20f, -0.13f, 0.48f + kick * 0.04f), m_ParryBlend);
            rotation = Quaternion.Slerp(rotation, Quaternion.Euler(-10 + kick * 15, -15, 58 + kick * 12), m_ParryBlend);
        }
    }
}
