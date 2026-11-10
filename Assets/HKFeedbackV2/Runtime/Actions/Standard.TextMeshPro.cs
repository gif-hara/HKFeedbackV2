namespace HKFeedbackV2
{
    public static partial class Standard
    {
        public static partial class TextMeshPro
        {
            [FeedbackAction]
            public static void SetText(TMPro.TMP_Text textMeshPro, string text)
            {
                textMeshPro.text = text;
            }

            [FeedbackAction]
            public static void SetFont(TMPro.TMP_Text textMeshPro, TMPro.TMP_FontAsset font)
            {
                textMeshPro.font = font;
            }

            [FeedbackAction]
            public static void SetFontSize(TMPro.TMP_Text textMeshPro, float fontSize)
            {
                textMeshPro.fontSize = fontSize;
            }

            [FeedbackAction]
            public static void SetFontStyle(TMPro.TMP_Text textMeshPro, TMPro.FontStyles fontStyle)
            {
                textMeshPro.fontStyle = fontStyle;
            }

            [FeedbackAction]
            public static void SetAlignment(TMPro.TMP_Text textMeshPro, TMPro.TextAlignmentOptions alignment)
            {
                textMeshPro.alignment = alignment;
            }

            [FeedbackAction]
            public static void SetAutoSizing(TMPro.TMP_Text textMeshPro, bool enableAutoSizing)
            {
                textMeshPro.enableAutoSizing = enableAutoSizing;
            }

            [FeedbackAction]
            public static void SetMaxVisibleCharacters(TMPro.TMP_Text textMeshPro, int maxVisibleCharacters)
            {
                textMeshPro.maxVisibleCharacters = maxVisibleCharacters;
            }
        }
    }
}
