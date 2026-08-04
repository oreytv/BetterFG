using BetterFG.Services;
using UnityEngine;

namespace BetterFG.Utilities
{
    public static class BeanAnimationUtil
    {
        public static Animator FindAnimator(GameObject bean)
        {
            if (bean == null) return null;
            var wrapper = bean.transform.Find(PlayerScaleService.WRAPPER_NAME);
            var character = (wrapper != null ? wrapper : bean.transform).Find("Character");
            return character?.GetComponent<Animator>();
        }

        public static void DriveLocomotion(Animator anim, Transform bean, Vector3 v)
        {
            if (anim == null || bean == null) return;

            float r = FallGuysCharacterController.GroundCheckSphereCastRadius;
            bool grounded = Physics.SphereCast(bean.position + Vector3.up * (r + 0.1f),
                r, Vector3.down, out var hit, 0.4f,
                FallGuysCharacterController.groundMask, QueryTriggerInteraction.Ignore);

            anim.SetBool(FallGuysCharacterController.groundedParam, grounded);
            anim.SetFloat(FallGuysCharacterController.slopeAngleParam,
                grounded ? Vector3.Angle(hit.normal, Vector3.up) : 0f);

            Vector3 local = Quaternion.Inverse(bean.rotation) * new Vector3(v.x, 0f, v.z);
            float mod = FallGuysCharacterController.AnimationVelocityParamModifier;
            anim.SetFloat(FallGuysCharacterController.zVelParam, new Vector2(v.x, v.z).magnitude * mod);
            anim.SetFloat(FallGuysCharacterController.xVelParam, local.x * mod);
            anim.SetFloat(FallGuysCharacterController.yVelParam, v.y * mod);
            anim.SetFloat(FallGuysCharacterController.airOnlyYVelParam, grounded ? 0f : v.y * mod);
        }
    }
}
