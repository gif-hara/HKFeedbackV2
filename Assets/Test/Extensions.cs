using UnityEngine;

namespace HKFeedbackV2.Test
{
    public static partial class Extensions
    {
        [FeedbackAction]
        public static void TestExtension(this TestSceneController self)
        {
            Debug.Log("TestExtension called");
        }
    }
}
