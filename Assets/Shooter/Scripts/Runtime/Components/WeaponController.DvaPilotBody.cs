using UnityEngine;
using UnityEngine.Rendering;

namespace Blocks.Gameplay.Shooter
{
    public partial class WeaponController
    {
        private GameObject m_DvaPilotBody;
        private Animator m_DvaPilotAnimator;
        private HumanPoseHandler m_DvaPilotSourcePose, m_DvaPilotTargetPose;
        private HumanPose m_DvaPilotPose;
        private Renderer[] m_DvaPilotRenderers;
        private bool m_DvaPilotBodyAttempted;

        private void UpdateDvaPilotBody()
        {
            bool visible = IsSpawned && CharacterId == 2 && DvaPilot && coreStats != null && coreStats.IsAlive;
            if (!visible)
            {
                if (m_DvaPilotBody != null) m_DvaPilotBody.SetActive(false);
                return;
            }
            if (m_Animator == null || !m_Animator.isHuman) return;
            if (m_DvaPilotBody == null && !m_DvaPilotBodyAttempted)
            {
                m_DvaPilotBodyAttempted = true;
                var prefab = Resources.Load<GameObject>("DvaPilot/PilotBody");
                if (prefab == null) { Debug.LogError("Dva pilot prefab is missing.", this); return; }
                m_DvaPilotBody = Instantiate(prefab, transform, false);
                m_DvaPilotAnimator = m_DvaPilotBody.GetComponent<Animator>();
                if (m_DvaPilotAnimator == null || !m_DvaPilotAnimator.isHuman)
                {
                    Debug.LogError("Dva pilot requires a valid humanoid avatar.", this);
                    Destroy(m_DvaPilotBody);
                    return;
                }
                m_DvaPilotAnimator.enabled = false;
                m_DvaPilotSourcePose = new HumanPoseHandler(m_Animator.avatar, m_Animator.transform);
                m_DvaPilotTargetPose = new HumanPoseHandler(m_DvaPilotAnimator.avatar, m_DvaPilotAnimator.transform);
                m_DvaPilotRenderers = m_DvaPilotBody.GetComponentsInChildren<Renderer>(true);
                foreach (var node in m_DvaPilotBody.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = gameObject.layer;
            }
            if (m_DvaPilotBody == null) return;
            m_DvaPilotBody.SetActive(true);
            m_DvaPilotSourcePose.GetHumanPose(ref m_DvaPilotPose);
            SetCowboyGrip(ref m_DvaPilotPose);
            m_DvaPilotTargetPose.SetHumanPose(ref m_DvaPilotPose);
            // Preserve locomotion and jump; only the pistol arm follows its aiming/reload pose.
            float recoil = Mathf.Sin(Mathf.Clamp01((Time.time - m_DvaPilotShotAt) / DvaPilotShotInterval) * Mathf.PI);
            SolveArm(m_DvaPilotAnimator, transform, HumanBodyBones.RightUpperArm,
                HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
                DvaPilotReloading ? new Vector3(.17f, 1.18f, .25f) : new Vector3(.18f, 1.4f, .55f - recoil * .04f), 1);
            AlignCowboyWrist(m_DvaPilotAnimator, transform.forward);
            foreach (var renderer in m_DvaPilotRenderers)
            {
                renderer.enabled = true;
                renderer.shadowCastingMode = IsOwner ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
            }
            foreach (var entry in m_DvaHiddenRenderers)
                if (entry.Key != null) entry.Key.enabled = false;
        }

        private void CleanupDvaPilotBody()
        {
            m_DvaPilotSourcePose?.Dispose();
            m_DvaPilotTargetPose?.Dispose();
            m_DvaPilotSourcePose = m_DvaPilotTargetPose = null;
            if (m_DvaPilotBody != null) Destroy(m_DvaPilotBody);
            m_DvaPilotBody = null;
            m_DvaPilotAnimator = null;
            m_DvaPilotBodyAttempted = false;
        }
    }
}
