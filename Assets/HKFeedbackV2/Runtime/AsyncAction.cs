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
        [SerializeReference, SubclassSelector]
        private IResolver<bool> forget = new Constant<bool>(true);

        public UniTask InvokeAsync(IContainer container, CancellationToken cancellationToken)
        {
            if (forget.Resolve(container))
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
