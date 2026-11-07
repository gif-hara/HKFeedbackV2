using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace HKFeedbackV2.Actions
{
    [Serializable]
    public sealed class Log : IAction
    {
        [SerializeReference, SubclassSelector]
        private IResolver<string> message;

        public UniTask InvokeAsync(IContainer container, CancellationToken cancellationToken)
        {
            Debug.Log(message.Resolve(container));
            return UniTask.CompletedTask;
        }
    }
}
