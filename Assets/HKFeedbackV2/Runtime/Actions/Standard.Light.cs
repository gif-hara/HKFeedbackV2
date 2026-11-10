namespace HKFeedbackV2
{
    public static partial class Standard
    {
        public static partial class Light
        {
            [FeedbackAction]
            public static void SetEnabled(UnityEngine.Light light, bool isEnabled)
            {
                light.enabled = isEnabled;
            }

            [FeedbackAction]
            public static void SetColor(UnityEngine.Light light, UnityEngine.Color color)
            {
                light.color = color;
            }

            [FeedbackAction]
            public static void SetIntensity(UnityEngine.Light light, float intensity)
            {
                light.intensity = intensity;
            }

            [FeedbackAction]
            public static void SetRange(UnityEngine.Light light, float range)
            {
                light.range = range;
            }
        }
    }
}
