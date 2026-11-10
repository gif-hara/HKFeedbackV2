namespace HKFeedbackV2
{
    public static partial class Standard
    {
        public static partial class Graphic
        {
            [FeedbackAction]
            public static void SetEnabled(UnityEngine.UI.Graphic graphic, bool isEnabled)
            {
                graphic.enabled = isEnabled;
            }

            [FeedbackAction]
            public static void SetColor(UnityEngine.UI.Graphic graphic, UnityEngine.Color color)
            {
                graphic.color = color;
            }

            [FeedbackAction]
            public static void SetAlpha(UnityEngine.UI.Graphic graphic, float alpha)
            {
                var color = graphic.color;
                color.a = alpha;
                graphic.color = color;
            }

            [FeedbackAction]
            public static void SetRaycastTarget(UnityEngine.UI.Graphic graphic, bool raycastTarget)
            {
                graphic.raycastTarget = raycastTarget;
            }

            [FeedbackAction]
            public static void SetMaterial(UnityEngine.UI.Graphic graphic, UnityEngine.Material material)
            {
                graphic.material = material;
            }
        }
    }
}
