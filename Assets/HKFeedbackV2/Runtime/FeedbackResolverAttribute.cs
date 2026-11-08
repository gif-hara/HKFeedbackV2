using System;

namespace HKFeedbackV2
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class FeedbackResolverAttribute : Attribute
    {
    }
}
