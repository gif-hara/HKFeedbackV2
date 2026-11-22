using System;

namespace HKFeedbackV2
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public sealed class FeedbackEvaluatorAttribute : Attribute
    {
    }
}
