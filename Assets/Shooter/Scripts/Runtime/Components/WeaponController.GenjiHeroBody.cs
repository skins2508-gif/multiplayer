using UnityEngine;
using Unity.Netcode;
using Blocks.Gameplay.Core;

namespace Blocks.Gameplay.Shooter
{
    public partial class WeaponController
    {
        private GameObject m_GenjiHeroBody;
        private Animator m_GenjiHeroAnimator;
        private HumanPoseHandler m_GenjiHeroSourcePose, m_GenjiHeroTargetPose;
        private HumanPose m_GenjiHeroPose;
        private SkinnedMeshRenderer[] m_GenjiHeroSkins;
        private GenjiLocomotionPose m_GenjiLocomotion;
        private JumpAbility m_GenjiJumpAbility;
        private CoreMovement m_GenjiMotor;
        private Vector3 m_GenjiPreviousPosition;
        private double m_GenjiCancelledAirJump = -101;
        private readonly NetworkVariable<double> m_GenjiAirJumpAt = new NetworkVariable<double>(-100,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private void InitializeGenjiLocomotion()
        {
            m_GenjiPreviousPosition = transform.position;
            m_GenjiMotor = GetComponent<CoreMovement>();
            m_GenjiJumpAbility = GetComponent<JumpAbility>();
            if (m_GenjiJumpAbility != null) m_GenjiJumpAbility.AirJumpPerformed += OnGenjiAirJump;
            if (IsOwner) m_GenjiAirJumpAt.Value = -100;
        }
        private void OnGenjiAirJump()
        {
            if (IsSpawned && IsOwner && CharacterId == 1)
                m_GenjiAirJumpAt.Value = NetworkManager.ServerTime.Time;
        }

        private void UpdateGenjiHeroBody()
        {
            Vector3 velocity = (transform.position - m_GenjiPreviousPosition) / Mathf.Max(.001f, Time.deltaTime);
            m_GenjiPreviousPosition = transform.position;
            if (!IsSpawned || CharacterId != 1)
            {
                if (m_GenjiHeroBody != null) m_GenjiHeroBody.SetActive(false);
                return;
            }
            if (m_Animator == null || !m_Animator.isHuman || m_SoldierBodyRenderer == null) return;
            if (m_GenjiHeroBody == null)
            {
                var prefab = Resources.Load<GameObject>("GenjiHero/GenjiHeroBody");
                if (prefab == null) return;
                m_GenjiHeroBody = Instantiate(prefab, transform, false);
                m_GenjiHeroAnimator = m_GenjiHeroBody.GetComponent<Animator>();
                if (m_GenjiHeroAnimator == null || !m_GenjiHeroAnimator.isHuman)
                { Destroy(m_GenjiHeroBody); return; }
                m_GenjiHeroAnimator.enabled = false;
                m_GenjiHeroSourcePose = new HumanPoseHandler(m_Animator.avatar, m_Animator.transform);
                m_GenjiHeroTargetPose = new HumanPoseHandler(m_GenjiHeroAnimator.avatar, m_GenjiHeroAnimator.transform);
                m_GenjiHeroSkins = m_GenjiHeroBody.GetComponentsInChildren<SkinnedMeshRenderer>();
                m_GenjiLocomotion = new GenjiLocomotionPose(m_GenjiHeroAnimator);
                foreach (var node in m_GenjiHeroBody.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = gameObject.layer;
            }
            m_GenjiHeroBody.SetActive(true);
            m_GenjiLocomotion.ResetRoot();
            m_GenjiHeroSourcePose.GetHumanPose(ref m_GenjiHeroPose);
            m_GenjiHeroTargetPose.SetHumanPose(ref m_GenjiHeroPose);
            if (DashSlashVisible) m_GenjiCancelledAirJump = m_GenjiAirJumpAt.Value;
            float airJumpElapsed = m_GenjiCancelledAirJump == m_GenjiAirJumpAt.Value ? -1 :
                (float)(NetworkManager.ServerTime.Time - m_GenjiAirJumpAt.Value);
            bool grounded = IsOwner && m_GenjiMotor != null ? m_GenjiMotor.IsGrounded : m_Animator.GetBool("Grounded");
            if (coreStats != null && coreStats.IsAlive)
                m_GenjiLocomotion.Apply(transform, velocity, grounded, airJumpElapsed, Time.deltaTime,
                    IsKatanaEquipped || ShurikenReloading || Time.time - m_NinjaVisualAttackStart < .8f);
            foreach (var skin in m_GenjiHeroSkins)
            {
                skin.enabled = m_SoldierBodyRenderer.enabled;
                skin.shadowCastingMode = m_SoldierBodyRenderer.shadowCastingMode;
            }
            m_SoldierBodyRenderer.forceRenderingOff = true;
            if (!IsOwner)
            {
                var hand = m_GenjiHeroAnimator.GetBoneTransform(HumanBodyBones.RightHand);
                if (m_NinjaKatanaVisual != null && m_NinjaKatanaVisual.transform.parent != hand)
                { m_NinjaKatanaVisual.transform.SetParent(hand, true); m_NinjaKatanaVisual.transform.localPosition = Vector3.zero; }
                if (m_NinjaShurikenVisual != null && m_NinjaShurikenVisual.transform.parent != hand)
                { m_NinjaShurikenVisual.transform.SetParent(hand, true); m_NinjaShurikenVisual.transform.localPosition = Vector3.zero; }
            }
        }
        private void CleanupGenjiHeroBody()
        {
            if (m_GenjiJumpAbility != null) m_GenjiJumpAbility.AirJumpPerformed -= OnGenjiAirJump;
            m_GenjiJumpAbility = null;
            m_GenjiLocomotion = null;
            m_GenjiHeroSourcePose?.Dispose();
            m_GenjiHeroTargetPose?.Dispose();
            m_GenjiHeroSourcePose = m_GenjiHeroTargetPose = null;
            if (m_GenjiHeroBody != null) Destroy(m_GenjiHeroBody);
            m_GenjiHeroBody = null;
            m_GenjiHeroAnimator = null;
        }
    }
}
