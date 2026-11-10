namespace HKFeedbackV2
{
    public static partial class Standard
    {
        public static partial class Camera
        {
            [FeedbackAction]
            public static void SetEnabled(UnityEngine.Camera camera, bool isEnabled)
            {
                camera.enabled = isEnabled;
            }

            [FeedbackAction]
            public static void SetFieldOfView(UnityEngine.Camera camera, float fieldOfView)
            {
                camera.fieldOfView = fieldOfView;
            }

            [FeedbackAction]
            public static void SetOrthographicSize(UnityEngine.Camera camera, float orthographicSize)
            {
                camera.orthographicSize = orthographicSize;
            }

            [FeedbackAction]
            public static void SetBackgroundColor(UnityEngine.Camera camera, UnityEngine.Color backgroundColor)
            {
                camera.backgroundColor = backgroundColor;
            }
        }
    }
}
