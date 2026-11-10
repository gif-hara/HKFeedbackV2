using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace HKFeedbackV2
{
    [Serializable]
    public sealed class FeedbackSequence : IAction
    {
        [SerializeReference] private IAction[] feedbacks;

        public IReadOnlyList<IAction> Feedbacks => feedbacks;

        public FeedbackSequence(IAction[] feedbacks)
        {
            this.feedbacks = feedbacks;
        }

        public UniTask InvokeAsync(IContainer container, CancellationToken cancellationToken) =>
            feedbacks.InvokeAsync(container, cancellationToken);
    }
}
