using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using HKFeedbackV2.Resolvers;
using UnityEngine;

namespace HKFeedbackV2.Actions
{
    [Serializable]
    public sealed class DelayFrame : IAction
    {
        [SerializeReference, SubclassSelector]
        private IResolver<int> frames = new Constant<int>(0);

        public UniTask InvokeAsync(IContainer container, CancellationToken cancellationToken)
        {
            return UniTask.DelayFrame(frames.Resolve(container), cancellationToken: cancellationToken);
        }
    }
}
