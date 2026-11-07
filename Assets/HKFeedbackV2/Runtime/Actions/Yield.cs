using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using HKFeedbackV2.Resolvers;
using UnityEngine;

namespace HKFeedbackV2.Actions
{
    [Serializable]
    public sealed class Yield : IAction
    {
        [SerializeReference, SubclassSelector]
        private IResolver<PlayerLoopTiming> playerLoopTiming = new Constant<PlayerLoopTiming>(PlayerLoopTiming.Update);

        public UniTask InvokeAsync(IContainer container, CancellationToken cancellationToken)
        {
            return UniTask.Yield(playerLoopTiming.Resolve(container), cancellationToken: cancellationToken);
        }
    }
}
