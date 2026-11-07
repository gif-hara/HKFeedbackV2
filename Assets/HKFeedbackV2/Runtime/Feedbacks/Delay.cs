using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using HKFeedbackV2.Resolvers;
using UnityEngine;

namespace HKFeedbackV2.Feedbacks
{
    [Serializable]
    public sealed class Delay : IAction
    {
        [SerializeReference, SubclassSelector]
        private IResolver<float> seconds = new Constant<float>(0.0f);

        public UniTask InvokeAsync(IContainer container, CancellationToken cancellationToken)
        {
            return UniTask.Delay(TimeSpan.FromSeconds(seconds.Resolve(container)), cancellationToken: cancellationToken);
        }
    }
}
