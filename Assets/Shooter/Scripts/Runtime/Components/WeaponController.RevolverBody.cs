using System;
using UnityEngine;

namespace Blocks.Gameplay.Shooter
{
    public partial class WeaponController
    {
        [Serializable] private class RevolverBodyKey { public float time; public Vector3 right; public Vector3 left; }
        [Serializable] private class RevolverBodyClip { public string name; public float duration; public RevolverBodyKey[] keys; }
        [Serializable] private class RevolverBodyData { public RevolverBodyClip[] clips; }
        private RevolverBodyData m_RevolverBodyData;
        private bool m_RevolverBodyLoaded;

        private void ApplyRevolverBodyMotion()
        {
            if (!IsSpawned || CharacterId != 3 || m_Animator == null || !m_Animator.isHuman ||
                (coreStats != null && !coreStats.IsAlive)) return;
            if (!m_RevolverBodyLoaded)
            {
                m_RevolverBodyLoaded = true;
                var data = Resources.Load<TextAsset>("Revolver/BodyMotions");
                if (data != null) m_RevolverBodyData = JsonUtility.FromJson<RevolverBodyData>(data.text);
            }
            if (m_RevolverBodyData?.clips == null) return;
            float elapsed = Time.time - m_RevolverRecoilAt;
            string state = RevolverReloading ? "Reload" : elapsed < (m_RevolverLastFan ? revolverFanInterval : .22f)
                ? (m_RevolverLastFan ? "Fan" : "Single") : "Ready";
            foreach (var clip in m_RevolverBodyData.clips)
            {
                if (clip.name != state || clip.keys == null || clip.keys.Length == 0) continue;
                float time = state == "Reload" ? (Time.time - m_RevolverReloadAt) / revolverReloadTime * clip.duration : elapsed;
                var first = clip.keys[0]; var last = clip.keys[clip.keys.Length - 1];
                for (int i=1; i<clip.keys.Length; i++)
                    if (time <= clip.keys[i].time) { first=clip.keys[i-1]; last=clip.keys[i]; break; }
                float t = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(first.time, last.time, time));
                Vector3 right = Vector3.Lerp(first.right,last.right,t);
                Vector3 left = Vector3.Lerp(first.left,last.left,t);
                m_CassidyRightHand=right;m_CassidyLeftHand=left;
                // Keep all existing leg, locomotion and jump animation. Only solve the two arms.
                SolveRevolverArm(HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm,HumanBodyBones.RightHand,right,1);
                SolveRevolverArm(HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.LeftHand,left,-1);
                break;
            }
        }
        private void SolveRevolverArm(HumanBodyBones upperId, HumanBodyBones lowerId, HumanBodyBones handId, Vector3 target, float side)
        {
            SolveArm(m_Animator, transform, upperId, lowerId, handId, target, side);
        }
        internal static void SolveArm(Animator animator, Transform root, HumanBodyBones upperId, HumanBodyBones lowerId, HumanBodyBones handId, Vector3 target, float side)
        {
            Transform upper=animator.GetBoneTransform(upperId), lower=animator.GetBoneTransform(lowerId), hand=animator.GetBoneTransform(handId);
            if(upper==null || lower==null || hand==null)return;
            Vector3 shoulder=upper.position;
            Vector3 goal=root.TransformPoint(target);
            float a=Vector3.Distance(shoulder,lower.position), b=Vector3.Distance(lower.position,hand.position);
            if(a<.001f || b<.001f)return;
            Vector3 direction=goal-shoulder;
            float distance=Mathf.Clamp(direction.magnitude,Mathf.Abs(a-b)+.001f,a+b-.001f);
            if(direction.sqrMagnitude<.00001f)return;
            direction.Normalize();goal=shoulder+direction*distance;
            Vector3 pole=Vector3.ProjectOnPlane(root.right*side-root.up,direction).normalized;
            float along=(a*a-b*b+distance*distance)/(2*distance);
            Vector3 elbow=shoulder+direction*along+pole*Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
            upper.rotation=Quaternion.FromToRotation(lower.position-shoulder,elbow-shoulder)*upper.rotation;
            lower.rotation=Quaternion.FromToRotation(hand.position-lower.position,goal-lower.position)*lower.rotation;
        }
    }
}
