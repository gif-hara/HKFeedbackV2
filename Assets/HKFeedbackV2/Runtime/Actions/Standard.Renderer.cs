namespace HKFeedbackV2
{
    public static partial class Standard
    {
        public static partial class Renderer
        {
            [FeedbackAction]
            public static void SetEnabled(UnityEngine.Renderer renderer, bool isEnabled)
            {
                renderer.enabled = isEnabled;
            }

            [FeedbackAction]
            public static void SetSharedMaterial(UnityEngine.Renderer renderer, UnityEngine.Material material)
            {
                renderer.sharedMaterial = material;
            }

            [FeedbackAction]
            public static void SetSortingOrder(UnityEngine.Renderer renderer, int sortingOrder)
            {
                renderer.sortingOrder = sortingOrder;
            }
        }
    }
}
