namespace HKFeedbackV2
{
    public static partial class Standard
    {
        public static partial class Collider
        {
            [FeedbackAction]
            public static void SetEnabled(UnityEngine.Collider collider, bool isEnabled)
            {
                collider.enabled = isEnabled;
            }

            [FeedbackAction]
            public static void SetIsTrigger(UnityEngine.Collider collider, bool isTrigger)
            {
                collider.isTrigger = isTrigger;
            }
        }
    }
}
