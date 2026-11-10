namespace HKFeedbackV2
{
    public static partial class Standard
    {
        public static partial class Transform
        {
            [FeedbackAction]
            public static void SetPosition(UnityEngine.Transform transform, UnityEngine.Vector3 position)
            {
                transform.position = position;
            }

            [FeedbackAction]
            public static void SetLocalPosition(UnityEngine.Transform transform, UnityEngine.Vector3 localPosition)
            {
                transform.localPosition = localPosition;
            }

            [FeedbackAction]
            public static void SetRotation(UnityEngine.Transform transform, UnityEngine.Vector3 eulerAngles)
            {
                transform.rotation = UnityEngine.Quaternion.Euler(eulerAngles);
            }

            [FeedbackAction]
            public static void SetLocalRotation(UnityEngine.Transform transform, UnityEngine.Vector3 eulerAngles)
            {
                transform.localRotation = UnityEngine.Quaternion.Euler(eulerAngles);
            }

            [FeedbackAction]
            public static void SetLocalScale(UnityEngine.Transform transform, UnityEngine.Vector3 localScale)
            {
                transform.localScale = localScale;
            }

            [FeedbackAction]
            public static void Translate(
                UnityEngine.Transform transform,
                UnityEngine.Vector3 translation,
                UnityEngine.Space space = UnityEngine.Space.Self)
            {
                transform.Translate(translation, space);
            }

            [FeedbackAction]
            public static void Rotate(
                UnityEngine.Transform transform,
                UnityEngine.Vector3 eulerAngles,
                UnityEngine.Space space = UnityEngine.Space.Self)
            {
                transform.Rotate(eulerAngles, space);
            }

            [FeedbackAction]
            public static void SetParent(UnityEngine.Transform transform, UnityEngine.Transform parent, bool worldPositionStays = true)
            {
                transform.SetParent(parent, worldPositionStays);
            }
        }
    }
}
