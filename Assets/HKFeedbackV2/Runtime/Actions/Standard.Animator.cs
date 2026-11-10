namespace HKFeedbackV2
{
    public static partial class Standard
    {
        public static partial class Animator
        {
            [FeedbackAction]
            public static void Play(
                UnityEngine.Animator animator,
                string stateName,
                int layer = -1,
                float normalizedTime = float.NegativeInfinity)
            {
                animator.Play(stateName, layer, normalizedTime);
            }

            [FeedbackAction]
            public static void SetTrigger(UnityEngine.Animator animator, string name)
            {
                animator.SetTrigger(name);
            }

            [FeedbackAction]
            public static void ResetTrigger(UnityEngine.Animator animator, string name)
            {
                animator.ResetTrigger(name);
            }

            [FeedbackAction]
            public static void SetBool(UnityEngine.Animator animator, string name, bool value)
            {
                animator.SetBool(name, value);
            }

            [FeedbackAction]
            public static void SetFloat(UnityEngine.Animator animator, string name, float value)
            {
                animator.SetFloat(name, value);
            }

            [FeedbackAction]
            public static void SetInteger(UnityEngine.Animator animator, string name, int value)
            {
                animator.SetInteger(name, value);
            }

            [FeedbackAction]
            public static void SetSpeed(UnityEngine.Animator animator, float speed)
            {
                animator.speed = speed;
            }
        }
    }
}
