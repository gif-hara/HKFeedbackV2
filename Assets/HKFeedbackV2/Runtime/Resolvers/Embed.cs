using System;
using UnityEngine;

namespace HKFeedbackV2.Resolvers
{
    [Serializable]
    public sealed class Embed<T> : IResolver<T>
    {
        [SerializeField]
        private T value;

        public T Resolve(IContainer container)
        {
            return value;
        }

        public Embed(T value)
        {
            this.value = value;
        }
    }
}
