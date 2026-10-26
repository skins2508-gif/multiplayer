using System;
using Blocks.Gameplay.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Blocks.Gameplay.Shooter
{
    public partial class WeaponController
    {
        private GameObject m_CowboyBody;
        private Animator m_CowboyAnimator;
        private SkinnedMeshRenderer m_CowboySkin;
        private HumanPoseHandler m_CowboySourcePose, m_CowboyTargetPose;
        private HumanPose m_CowboyPose;
        private void UpdateCowboyBody()
        {
            if(!IsSpawned || m_Animator==null || !m_Animator.isHuman || m_SoldierBodyRenderer==null)return;
            if(CharacterId!=3)
            {
                if(m_CowboyBody!=null)m_CowboyBody.SetActive(false);
                m_SoldierBodyRenderer.forceRenderingOff=false;
                return;
            }
            if(m_CowboyBody==null)
            {
                var prefab=Resources.Load<GameObject>("Cowboy/CowboyBody");if(prefab==null)return;
                m_CowboyBody=Instantiate(prefab,transform,false);
                m_CowboyAnimator=m_CowboyBody.GetComponent<Animator>();
                m_CowboySkin=m_CowboyBody.GetComponentInChildren<SkinnedMeshRenderer>();
                if(m_CowboyAnimator==null || !m_CowboyAnimator.isHuman){Destroy(m_CowboyBody);return;}
                m_CowboySourcePose=new HumanPoseHandler(m_Animator.avatar,m_Animator.transform);
                m_CowboyTargetPose=new HumanPoseHandler(m_CowboyAnimator.avatar,m_CowboyAnimator.transform);
            }
            m_CowboyBody.SetActive(true);
            m_CowboySourcePose.GetHumanPose(ref m_CowboyPose);
            SetCowboyGrip(ref m_CowboyPose);
            m_CowboyTargetPose.SetHumanPose(ref m_CowboyPose);
            if(!CassidyRolling && coreStats!=null && coreStats.IsAlive)
            {
                SolveArm(m_CowboyAnimator,transform,HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm,HumanBodyBones.RightHand,m_CassidyRightHand,1);
                SolveArm(m_CowboyAnimator,transform,HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.LeftHand,m_CassidyLeftHand,-1);
                AlignCowboyWrist(m_CowboyAnimator,transform.forward);
            }
            m_CowboySkin.enabled=m_SoldierBodyRenderer.enabled;
            m_CowboySkin.shadowCastingMode=m_SoldierBodyRenderer.shadowCastingMode;
            m_SoldierBodyRenderer.forceRenderingOff=true;
        }
        private Vector3 m_CassidyRightHand=new Vector3(.24f,1.35f,.48f),m_CassidyLeftHand=new Vector3(-.22f,1.08f,.18f);
        public static void SetCowboyGrip(ref HumanPose pose)
        {
            for(int i=0;i<HumanTrait.MuscleCount;i++)
            {
                string name=HumanTrait.MuscleName[i];
                if(name.StartsWith("Right ") && name.Contains("Stretched"))pose.muscles[i]=name.Contains("Thumb")?-.15f:name.Contains("Index")?-.35f:-.7f;
                if(name=="Right Hand Down-Up" || name=="Right Hand In-Out")pose.muscles[i]=0;
            }
        }
        public static void AlignCowboyWrist(Animator animator,Vector3 forward)
        {
            var hand=animator.GetBoneTransform(HumanBodyBones.RightHand);var middle=animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            if(hand==null || middle==null || forward.sqrMagnitude<.0001f)return;
            var index=animator.GetBoneTransform(HumanBodyBones.RightIndexProximal);
            var ring=animator.GetBoneTransform(HumanBodyBones.RightRingProximal);
            Vector3 fingers=middle.position-hand.position;
            Vector3 across=index!=null && ring!=null?index.position-ring.position:animator.transform.up;
            if(fingers.sqrMagnitude<.0001f || Vector3.Cross(fingers,across).sqrMagnitude<.00000001f)return;
            // Align both finger direction and wrist roll so the grip stays upright.
            hand.rotation=Quaternion.LookRotation(forward,animator.transform.up)*
                Quaternion.Inverse(Quaternion.LookRotation(fingers,across))*hand.rotation;
        }
        [SerializeField, Min(.1f)] private float cassidyRollDistance = 5f;

        private const float CassidyRollDuration = .72f;
        private readonly NetworkVariable<double> m_CassidyRollStart = new NetworkVariable<double>(-100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<Vector3> m_CassidyRollDirection = new NetworkVariable<Vector3>(Vector3.forward, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private float m_CassidyRollMovedTime = CassidyRollDuration;
        private CoreMovement m_CassidyMotor;
        [Serializable] private class RollKey { public float time, roll, tuck, drop; }
        [Serializable] private class RollClip { public float duration; public RollKey[] keys; }
        private static RollClip s_RollClip;
        private Transform[] m_RollBones;
        private Vector3[] m_RollPositions;
        private Quaternion[] m_RollRotations;
        private bool m_RollPoseApplied;
        private float CassidyRollElapsed => NetworkManager!=null ? (float)(NetworkManager.ServerTime.Time-m_CassidyRollStart.Value) : 100;
        public bool CassidyRolling => IsSpawned && CharacterId==3 && coreStats!=null && coreStats.IsAlive && CassidyRollElapsed>=0 && CassidyRollElapsed<CassidyRollDuration;

        private bool CassidyRollHasMovement => IsOwner && IsSpawned && CharacterId==3 && coreStats!=null && coreStats.IsAlive &&
            m_CassidyRollMovedTime<CassidyRollDuration && CassidyRollElapsed>=0 && CassidyRollElapsed<CassidyRollDuration+.5f;

        private void InitializeCassidyRoll()
        {
            m_CassidyMotor=GetComponent<CoreMovement>();
            m_CharacterId.OnValueChanged+=OnCassidyRollCharacter;
            OnCassidyRollCharacter(CharacterId,CharacterId);
            if(IsOwner)m_CassidyRollStart.Value=-100;
        }
        private void OnCassidyRollCharacter(int previous,int next)
        {
            RestoreCassidyRollPose();
            if(m_CassidyMotor!=null){m_CassidyMotor.SprintAllowed=next!=3;if(next==3)m_CassidyMotor.SetSprintState(false);}
            if(IsOwner && IsSpawned)m_CassidyRollStart.Value=-100;
        }
        private void UpdateCassidyRoll()
        {
            if(!IsSpawned || CharacterId!=3)return;
            if(m_CassidyMotor!=null)m_CassidyMotor.SetSprintState(false);
            if(coreStats==null || !coreStats.IsAlive || (ShooterEscortMatch.Instance!=null && !ShooterEscortMatch.Instance.CombatAllowed))
            {m_CassidyRollStart.Value=-100;return;}
            if(Keyboard.current!=null && Keyboard.current.leftShiftKey.wasPressedThisFrame)TryCassidyRoll();
        }
        public bool TryCassidyRoll()
        {
            if(!CanUseRevolver || m_CassidyMotor==null || !m_CassidyMotor.IsMovementEnabled || !m_CassidyMotor.IsGrounded ||
                DeadeyeActive || RevolverReloading || m_RevolverFanning || Time.time<m_RevolverNextShot ||
                (ShooterEscortMatch.Instance!=null && !ShooterEscortMatch.Instance.CombatAllowed))return false;
            var view=Camera.main!=null?Camera.main.transform:transform;
            var forward=Vector3.ProjectOnPlane(view.forward,Vector3.up).normalized;
            if(forward.sqrMagnitude<.1f)forward=transform.forward;
            Vector2 input=m_CassidyMotor.MoveInput;
            Vector3 direction=forward*input.y+Vector3.Cross(Vector3.up,forward)*input.x;
            if(direction.sqrMagnitude<.01f)direction=forward;
            m_CassidyRollDirection.Value=direction.normalized;
            m_CassidyRollStart.Value=NetworkManager.ServerTime.Time;
            m_CassidyRollMovedTime=0;

            m_CassidyMotor.SetSprintState(false);
            return true;
        }
        private Vector3 CassidyRollMovement(Vector3 original)
        {
            // Integrate the portion of this frame inside the roll; wall collision still
            // runs through CoreMovement's CharacterController.Move.
            float elapsed=Mathf.Clamp(CassidyRollElapsed,0,CassidyRollDuration);
            float step=Mathf.Max(0,elapsed-m_CassidyRollMovedTime);
            m_CassidyRollMovedTime=elapsed;
            return m_CassidyRollDirection.Value*(cassidyRollDistance/CassidyRollDuration*step)+Vector3.up*original.y;
        }
        private void RestoreCassidyRollPose()
        {
            if(!m_RollPoseApplied)return;
            for(int i=0;i<m_RollBones.Length;i++)if(m_RollBones[i]!=null){m_RollBones[i].localPosition=m_RollPositions[i];m_RollBones[i].localRotation=m_RollRotations[i];}
            m_RollPoseApplied=false;
        }
        private void ApplyCassidyRollPose()
        {
            if(!CassidyRolling || m_Animator==null || !m_Animator.isHuman)return;
            if(s_RollClip==null){var data=Resources.Load<TextAsset>("Cowboy/CombatRoll");if(data!=null)s_RollClip=JsonUtility.FromJson<RollClip>(data.text);}
            if(s_RollClip?.keys==null || s_RollClip.keys.Length<2)return;
            if(m_RollBones==null){m_RollBones=m_Animator.GetBoneTransform(HumanBodyBones.Hips).GetComponentsInChildren<Transform>();m_RollPositions=new Vector3[m_RollBones.Length];m_RollRotations=new Quaternion[m_RollBones.Length];}
            for(int i=0;i<m_RollBones.Length;i++){m_RollPositions[i]=m_RollBones[i].localPosition;m_RollRotations[i]=m_RollBones[i].localRotation;}
            m_RollPoseApplied=true;
            var keys=s_RollClip.keys;float time=CassidyRollElapsed;RollKey a=keys[0],b=keys[keys.Length-1];
            for(int i=1;i<keys.Length;i++)if(time<=keys[i].time){a=keys[i-1];b=keys[i];break;}
            float t=Mathf.SmoothStep(0,1,Mathf.InverseLerp(a.time,b.time,time));
            PoseCassidyRoll(m_Animator,transform,Mathf.Lerp(a.roll,b.roll,t),Mathf.Lerp(a.tuck,b.tuck,t),Mathf.Lerp(a.drop,b.drop,t),m_CassidyRollDirection.Value);
        }
        public static void PoseCassidyRoll(Animator animator,Transform root,float roll,float tuck,float drop,Vector3 direction)
        {
            Vector3 axis=root.right;
            BendRollBone(animator,HumanBodyBones.LeftUpperLeg,axis,-105*tuck);BendRollBone(animator,HumanBodyBones.RightUpperLeg,axis,-95*tuck);
            BendRollBone(animator,HumanBodyBones.LeftLowerLeg,axis,140*tuck);BendRollBone(animator,HumanBodyBones.RightLowerLeg,axis,135*tuck);
            BendRollBone(animator,HumanBodyBones.Spine,axis,28*tuck);BendRollBone(animator,HumanBodyBones.Chest,axis,22*tuck);BendRollBone(animator,HumanBodyBones.Neck,axis,20*tuck);
            TuckRollArm(animator,root,HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.LeftHand,-1,tuck);
            TuckRollArm(animator,root,HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm,HumanBodyBones.RightHand,1,tuck);
            var hips=animator.GetBoneTransform(HumanBodyBones.Hips);
            float yaw=Vector3.SignedAngle(root.forward,direction,Vector3.up);
            float blend=Mathf.Clamp01(Mathf.Min(roll/35,(360-roll)/35));
            hips.rotation=Quaternion.AngleAxis(yaw*blend,Vector3.up)*Quaternion.AngleAxis(roll,axis)*hips.rotation;
            hips.position-=Vector3.up*drop;
        }
        private static void BendRollBone(Animator animator,HumanBodyBones bone,Vector3 axis,float angle)
        {var part=animator.GetBoneTransform(bone);if(part!=null)part.rotation=Quaternion.AngleAxis(angle,axis)*part.rotation;}
        private static void TuckRollArm(Animator animator,Transform root,HumanBodyBones upper,HumanBodyBones lower,HumanBodyBones hand,float side,float blend)
        {
            var a=animator.GetBoneTransform(upper);var b=animator.GetBoneTransform(lower);var ar=a.localRotation;var br=b.localRotation;
            SolveArm(animator,root,upper,lower,hand,new Vector3(side*.13f,1.20f,.18f),side);
            a.localRotation=Quaternion.Slerp(ar,a.localRotation,blend);b.localRotation=Quaternion.Slerp(br,b.localRotation,blend);
        }
        private void CleanupCassidyRoll()
        {
            RestoreCassidyRollPose();m_CharacterId.OnValueChanged-=OnCassidyRollCharacter;
            m_CowboySourcePose?.Dispose();m_CowboyTargetPose?.Dispose();m_CowboySourcePose=null;m_CowboyTargetPose=null;
            if(m_SoldierBodyRenderer!=null)m_SoldierBodyRenderer.forceRenderingOff=false;
            if(m_CowboyBody!=null)Destroy(m_CowboyBody);
            if(m_CassidyMotor!=null)m_CassidyMotor.SprintAllowed=true;
        }
    }
}

