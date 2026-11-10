using System;

namespace HKFeedbackV2.Resolvers
{
    [Serializable]
    public sealed class DefaultContainer<T> : IResolver<IContainer>
    {
        IContainer IResolver<IContainer>.Resolve(IContainer container) => container;
    }
}
