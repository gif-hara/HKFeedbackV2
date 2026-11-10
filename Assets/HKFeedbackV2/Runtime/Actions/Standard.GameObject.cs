namespace HKFeedbackV2
{
    public static partial class Standard
    {
        public static partial class GameObject
        {
            [FeedbackAction]
            public static void SetActive(UnityEngine.GameObject gameObject, bool isActive)
            {
                gameObject.SetActive(isActive);
            }

            [FeedbackAction]
            public static void SetName(UnityEngine.GameObject gameObject, string name)
            {
                gameObject.name = name;
            }

            [FeedbackAction]
            public static void SetLayer(UnityEngine.GameObject gameObject, int layer)
            {
                gameObject.layer = layer;
            }

            [FeedbackAction]
            public static void SetTag(UnityEngine.GameObject gameObject, string tag = "Untagged")
            {
                gameObject.tag = tag;
            }

            [FeedbackAction]
            public static void Instantiate(UnityEngine.GameObject original)
            {
                UnityEngine.Object.Instantiate(original);
            }

            [FeedbackAction]
            public static void Instantiate(UnityEngine.GameObject original, UnityEngine.Vector3 position, UnityEngine.Vector3 eulerAngles)
            {
                UnityEngine.Object.Instantiate(original, position, UnityEngine.Quaternion.Euler(eulerAngles));
            }

            [FeedbackAction]
            public static void Instantiate(UnityEngine.GameObject original, UnityEngine.Transform parent, bool worldPositionStays = false)
            {
                UnityEngine.Object.Instantiate(original, parent, worldPositionStays);
            }

            [FeedbackAction]
            public static void Destroy(UnityEngine.GameObject gameObject, float delay = 0f)
            {
                UnityEngine.Object.Destroy(gameObject, delay);
            }
        }
    }
}
