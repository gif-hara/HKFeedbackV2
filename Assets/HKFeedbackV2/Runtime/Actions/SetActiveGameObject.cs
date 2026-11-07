using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using HKFeedbackV2.Resolvers;
using UnityEngine;

namespace HKFeedbackV2.Actions
{
    [Serializable]
    public sealed class SetActiveGameObject : IAction
    {
        [SerializeReference, SubclassSelector]
        private IResolver<GameObject> gameObject;

        [SerializeReference, SubclassSelector]
        private IResolver<bool> activeState = new Constant<bool>(true);

        public UniTask InvokeAsync(IContainer container, CancellationToken cancellationToken)
        {
            gameObject.Resolve(container).SetActive(activeState.Resolve(container));
            return UniTask.CompletedTask;
        }
    }
}
