using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using HKFeedbackV2.Resolvers;
using UnityEngine;

namespace HKFeedbackV2.Feedbacks
{
    [Serializable]
    public sealed class SyncTransform : IFeedback
    {
        [SerializeReference, SubclassSelector]
        private IResolver<Transform> transform;

        [SerializeReference, SubclassSelector]
        private IResolver<Transform> target;

        [SerializeField, SubclassSelector]
        private IResolver<bool> syncPosition = new Constant<bool>(true);

        [SerializeField, SubclassSelector]
        private IResolver<bool> syncRotation = new Constant<bool>(true);

        [SerializeReference, SubclassSelector]
        private IResolver<bool> syncScale = new Constant<bool>(true);

        public UniTask InvokeAsync(IContainer container, CancellationToken cancellationToken)
        {
            var transform = this.transform.Resolve(container);
            var target = this.target.Resolve(container);
            if (syncPosition.Resolve(container))
            {
                transform.position = target.position;
            }
            if (syncRotation.Resolve(container))
            {
                transform.rotation = target.rotation;
            }
            if (syncScale.Resolve(container))
            {
                transform.localScale = target.localScale;
            }

            return UniTask.CompletedTask;
        }
    }
}
