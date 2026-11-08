using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using HKFeedbackV2.Resolvers;
using UnityEngine;

namespace HKFeedbackV2.Actions
{
    [Serializable]
    public abstract class AsyncAction : IAction
    {
        [SerializeReference]
#if HK_FEEDBACK_V2_SERIALIZE_REFERENCE_EXTENSIONS
        [SubclassSelector]
#endif
        private IResolver<bool> isWait = new SerializeField<bool>(true);

        public UniTask InvokeAsync(IContainer container, CancellationToken cancellationToken)
        {
            if (!isWait.Resolve(container))
            {
                InvokeAsyncInternal(container, cancellationToken).Forget();
                return UniTask.CompletedTask;
            }
            else
            {
                return InvokeAsyncInternal(container, cancellationToken);
            }
        }

        protected abstract UniTask InvokeAsyncInternal(IContainer container, CancellationToken cancellationToken);
    }
}
