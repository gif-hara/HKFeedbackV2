using System;
using UnityEngine;

namespace HKFeedbackV2.Resolvers
{
    [Serializable]
    public sealed class ContainerAnonymous<T> : IResolver<T>
    {
        [SerializeReference, SubclassSelector]
        private IResolver<bool> canBeNull = new Constant<bool>(false);

        public T Resolve(IContainer container)
        {
            if (canBeNull.Resolve(container))
            {
                container.TryResolve<T>(out var value);
                return value;
            }
            else
            {
                return container.Resolve<T>();
            }
        }

        public ContainerAnonymous()
        {
        }
    }
}
