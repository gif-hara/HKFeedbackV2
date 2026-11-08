namespace HKFeedbackV2
{
    public partial class Standard
    {
        public partial class Debug
        {
            [FeedbackAction]
            public void Log(string message)
            {
                UnityEngine.Debug.Log(message);
            }
        }
    }
}
