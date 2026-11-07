using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace HKFeedbackV2.Test
{
    public static class FeedbackActionExamples
    {
        [FeedbackAction]
        public static void Log(string message = "Hello from a generated action")
        {
            Debug.Log(message);
        }

        [FeedbackAction]
        public static UniTask Wait(float seconds = 0.1f, CancellationToken cancellationToken = default)
        {
            return UniTask.Delay(TimeSpan.FromSeconds(seconds), cancellationToken: cancellationToken);
        }
    }
}
