namespace HKFeedbackV2
{
    public static partial class Standard
    {
        public static partial class SpriteRenderer
        {
            [FeedbackAction]
            public static void RegisterSpriteRenderer(IContainer container, RegisterElement<UnityEngine.SpriteRenderer>[] elements)
            {
                RegisterTargets(container, elements);
            }

            [FeedbackAction]
            public static void SetSprite(UnityEngine.SpriteRenderer spriteRenderer, UnityEngine.Sprite sprite)
            {
                spriteRenderer.sprite = sprite;
            }

            [FeedbackAction]
            public static void SetColor(UnityEngine.SpriteRenderer spriteRenderer, UnityEngine.Color color)
            {
                spriteRenderer.color = color;
            }
        }
    }
}
