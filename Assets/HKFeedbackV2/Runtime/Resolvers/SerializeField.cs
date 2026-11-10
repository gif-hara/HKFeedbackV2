using System;

namespace HKFeedbackV2.Resolvers
{
    [Serializable]
    public sealed class SerializeField<T> : IResolver<T>
    {
        [UnityEngine.SerializeField]
        private T value;

        public T Resolve(IContainer container)
        {
            return value;
        }

        public SerializeField(T value)
        {
            this.value = value;
        }

        public SerializeField()
        {
            this.value = default!;
        }
    }
}
