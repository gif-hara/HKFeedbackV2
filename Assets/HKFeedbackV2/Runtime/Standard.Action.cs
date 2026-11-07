using UnityEngine;

namespace HKFeedbackV2
{
    public partial class Standard
    {
        [FeedbackAction]
        public void Log(string message)
        {
            Debug.Log(message);
        }

        [FeedbackAction]
        public void Log(string message, Object context)
        {
            Debug.Log(message, context);
        }
    }
}
