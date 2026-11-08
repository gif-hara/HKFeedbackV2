using System;
using UnityEngine;

namespace HKFeedbackV2.Resolvers
{
    [Serializable]
    public sealed class Anonymous<T> : IResolver<T>
    {
        [SerializeReference]
#if HK_FEEDBACK_V2_SERIALIZE_REFERENCE_EXTENSIONS
        [SubclassSelector]
#endif
        private IResolver<bool> canBeNull = new Embed<bool>(false);

        public T Resolve(IContainer container)
        {
            if (canBeNull.Resolve(container))
            {
                container.TryResolve<T>(ContainerKey.Anonymous, out var value);
                return value;
            }
            else
            {
                return container.Resolve<T>(ContainerKey.Anonymous);
            }
        }

        public Anonymous()
        {
        }
    }
}
