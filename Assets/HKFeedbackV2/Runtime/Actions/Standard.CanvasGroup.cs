namespace HKFeedbackV2
{
    public static partial class Standard
    {
        public static partial class CanvasGroup
        {
            [FeedbackAction]
            public static void SetAlpha(UnityEngine.CanvasGroup canvasGroup, float alpha)
            {
                canvasGroup.alpha = alpha;
            }

            [FeedbackAction]
            public static void SetInteractable(UnityEngine.CanvasGroup canvasGroup, bool isInteractable)
            {
                canvasGroup.interactable = isInteractable;
            }

            [FeedbackAction]
            public static void SetBlocksRaycasts(UnityEngine.CanvasGroup canvasGroup, bool blocksRaycasts)
            {
                canvasGroup.blocksRaycasts = blocksRaycasts;
            }
        }
    }
}
