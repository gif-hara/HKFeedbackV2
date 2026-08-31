using System;
using UnityEngine;

namespace HKFeedbackV2.Resolvers
{
    [Serializable]
    public sealed class ContainerWithKey<T> : IResolver<T>
    {
        [SerializeReference, SubclassSelector]
        private IResolver<bool> canBeNull = new Constant<bool>(false);

        [SerializeReference, SubclassSelector]
        private IResolver<ContainerKey> key = new Constant<ContainerKey>(null);

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

        public ContainerWithKey()
        {
        }
    }
}
