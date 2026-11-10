using System;

namespace HKFeedbackV2.Resolvers
{
    [Serializable]
    public sealed class SerializeReference<T> : IResolver<T>
    {
        [UnityEngine.SerializeReference]
#if HK_FEEDBACK_V2_SERIALIZE_REFERENCE_EXTENSIONS
        [SubclassSelector]
#endif
        private T value;

        public T Resolve(IContainer container)
        {
            return value;
        }

        public SerializeReference(T value)
        {
            this.value = value;
        }

        public SerializeReference()
        {
            this.value = default!;
        }
    }
}
