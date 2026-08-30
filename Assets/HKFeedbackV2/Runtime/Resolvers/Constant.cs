using System;
using UnityEngine;

namespace HKFeedbackV2.Resolvers
{
    [Serializable]
    public sealed class Constant<T> : IResolver<T>
    {
        [SerializeField]
        private T value;

        public T Resolve(IContainer container)
        {
            return value;
        }
    }
}
