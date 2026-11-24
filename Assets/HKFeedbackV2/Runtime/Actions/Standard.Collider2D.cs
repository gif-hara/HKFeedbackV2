namespace HKFeedbackV2
{
    public static partial class Standard
    {
        public static partial class Collider2D
        {
            [FeedbackAction]
            public static void RegisterCollider2D(IContainer container, RegisterElement<UnityEngine.Collider2D>[] elements)
            {
                RegisterTargets(container, elements);
            }

            [FeedbackAction]
            public static void SetEnabled(UnityEngine.Collider2D collider, bool isEnabled)
            {
                collider.enabled = isEnabled;
            }

            [FeedbackAction]
            public static void SetIsTrigger(UnityEngine.Collider2D collider, bool isTrigger)
            {
                collider.isTrigger = isTrigger;
            }
        }
    }
}
