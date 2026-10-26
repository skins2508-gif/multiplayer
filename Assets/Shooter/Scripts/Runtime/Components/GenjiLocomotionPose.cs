using UnityEngine;

namespace Blocks.Gameplay.Shooter
{
    // Shared visual animation for the player, remote players and AI Genji bodies.
    public sealed class GenjiLocomotionPose
    {
        public const float FlipDuration = .58f;
        readonly Animator animator;
        readonly Transform body;
        readonly Vector3 restPosition;
        readonly Quaternion restRotation;
        readonly Transform[] legs = new Transform[6];
        readonly Quaternion[] restLegs = new Quaternion[6];
        float phase, landingAge = 1;
        Vector3 smoothedVelocity;
        bool wasGrounded = true, initialized;

        public GenjiLocomotionPose(Animator animator)
        {
            this.animator = animator; body = animator.transform;
            restPosition = body.localPosition; restRotation = body.localRotation;
            var ids = new[] { HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
                HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot };
            for (int i = 0; i < ids.Length; i++)
            { legs[i] = animator.GetBoneTransform(ids[i]); restLegs[i] = legs[i].localRotation; }
        }
        public void ResetRoot()
        { body.localPosition = restPosition; body.localRotation = restRotation; }
        public void Apply(Transform actor, Vector3 velocity, bool grounded, float airJumpElapsed, float deltaTime, bool preserveArms)
        {
            ResetRoot();
            float dt = Mathf.Clamp(deltaTime, 0, .1f);
            velocity.y = 0;
            if (velocity.sqrMagnitude > 225) velocity = Vector3.zero; // Respawn/teleport is not a running stride.
            smoothedVelocity = Vector3.Lerp(smoothedVelocity, velocity, 1 - Mathf.Exp(-14 * dt));
            float speed = smoothedVelocity.magnitude;
            Vector3 local = actor.InverseTransformDirection(smoothedVelocity);
            float forward = speed > .05f ? local.z / speed : 1;
            float lateral = speed > .05f ? local.x / speed : 0;
            phase += speed * dt / Mathf.Lerp(2.1f, 3.2f, Mathf.Clamp01(speed / 6)) * Mathf.PI * 2;
            if (initialized && grounded && !wasGrounded) landingAge = 0;
            initialized = true; wasGrounded = grounded; landingAge += dt;
            float landing = Mathf.Sin(Mathf.Clamp01(landingAge / .2f) * Mathf.PI);
            bool flipping = !grounded && airJumpElapsed >= 0 && airJumpElapsed < FlipDuration;
            float t = Mathf.Clamp01(airJumpElapsed / FlipDuration);
            float tuck = flipping ? Mathf.Sin(t * Mathf.PI) : 0;
            float weight = grounded ? Mathf.Clamp01(speed / .8f + landing) : 1;
            for (int i = 0; i < 6; i++) legs[i].localRotation = Quaternion.Slerp(legs[i].localRotation, restLegs[i], weight);
            for (int side = 0; side < 2; side++)
            {
                float wave = Mathf.Sin(phase + side * Mathf.PI);
                float stride = wave * Mathf.Lerp(24, 43, Mathf.Clamp01(speed / 6));
                float thigh, knee, ankle;
                if (grounded)
                {
                    thigh = (-stride * forward - 7) * Mathf.Clamp01(speed / .8f) - 15 * landing;
                    knee = (12 + 55 * Mathf.Max(0, wave)) * Mathf.Clamp01(speed / .8f) + 30 * landing;
                    ankle = -knee * .35f;
                }
                else
                {
                    thigh = Mathf.Lerp(side == 0 ? -28 : 12, side == 0 ? -100 : -82, tuck);
                    knee = Mathf.Lerp(side == 0 ? 50 : 24, 135, tuck);
                    ankle = -20 - 15 * tuck;
                }
                Rotate(legs[side * 3], actor.right, thigh);
                Rotate(legs[side * 3], actor.forward, -stride * lateral * .6f);
                Rotate(legs[side * 3 + 1], actor.right, knee);
                Rotate(legs[side * 3 + 2], actor.right, ankle);
            }
            if (!preserveArms)
            {
                float armSwing = grounded ? Mathf.Sin(phase) * Mathf.Clamp01(speed / 2) : 0;
                Vector3 right = flipping ? new Vector3(.30f, 1.28f, .23f) :
                    new Vector3(.30f, 1.28f + .035f * armSwing, .28f - .10f * armSwing);
                Vector3 left = flipping ? new Vector3(-.30f, 1.24f, .25f) :
                    new Vector3(-.32f, 1.16f - .055f * armSwing, .24f + .13f * armSwing);
                WeaponController.SolveArm(animator, actor, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm,
                    HumanBodyBones.RightHand, right, 1);
                WeaponController.SolveArm(animator, actor, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm,
                    HumanBodyBones.LeftHand, left, -1);
                WeaponController.AlignCowboyWrist(animator, actor.forward);
            }
            float roll = flipping ? 360 * Mathf.SmoothStep(0, 1, t) : 0;
            float lean = grounded ? Mathf.Clamp(local.z * 1.6f, -5, 10) : 0;
            Quaternion turn = Quaternion.Euler(roll + lean, 0, grounded ? Mathf.Clamp(-local.x * 1.8f, -8, 8) : 0);
            Vector3 pivot = Vector3.up * .95f;
            body.localRotation = turn * restRotation;
            body.localPosition = restPosition + pivot - turn * pivot - Vector3.up * (.035f * landing);
        }
        static void Rotate(Transform bone, Vector3 axis, float degrees)
        { bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation; }
    }
}
