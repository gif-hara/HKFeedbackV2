using System;
using UnityEngine;

namespace HKFeedbackV2.Resolvers
{
    [Serializable]
    public sealed class ContainerKey<T> : IResolver<T>
    {
        [SerializeReference, SubclassSelector]
        private IResolver<bool> canBeNull = new Embed<bool>(false);

        [SerializeReference, SubclassSelector]
        private IResolver<ContainerKey> key = new Embed<ContainerKey>(null);

        public T Resolve(IContainer container)
        {
            if (canBeNull.Resolve(container))
            {
                container.TryResolve<T>(key.Resolve(container), out var value);
                return value;
            }
            else
            {
                return container.Resolve<T>(key.Resolve(container));
            }
        }

        public ContainerKey()
        {
        }
    }
}
