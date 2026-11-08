namespace HKFeedbackV2
{
    public static partial class Standard
    {
        public static partial class Debug
        {
            [FeedbackAction]
            public static void Log(string message)
            {
                UnityEngine.Debug.Log(message);
            }
        }
    }
}
