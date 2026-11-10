namespace HKFeedbackV2
{
    public static partial class Standard
    {
        public static partial class RectTransform
        {
            [FeedbackAction]
            public static void SetAnchoredPosition(UnityEngine.RectTransform rectTransform, UnityEngine.Vector2 anchoredPosition)
            {
                rectTransform.anchoredPosition = anchoredPosition;
            }

            [FeedbackAction]
            public static void SetSizeDelta(UnityEngine.RectTransform rectTransform, UnityEngine.Vector2 sizeDelta)
            {
                rectTransform.sizeDelta = sizeDelta;
            }

            [FeedbackAction]
            public static void SetPivot(UnityEngine.RectTransform rectTransform, UnityEngine.Vector2 pivot)
            {
                rectTransform.pivot = pivot;
            }
        }
    }
}
