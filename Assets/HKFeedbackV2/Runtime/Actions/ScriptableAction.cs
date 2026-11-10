using System.Threading;
using Cysharp.Threading.Tasks;
using HKFeedbackV2.Resolvers;
using UnityEngine;

namespace HKFeedbackV2.Actions
{
    [CreateAssetMenu(fileName = "ScriptableAction", menuName = "HKFeedbackV2/ScriptableAction")]
    public sealed class ScriptableAction : UnityEngine.ScriptableObject, IAction
    {
        [SerializeReference]
#if HK_FEEDBACK_V2_SERIALIZE_REFERENCE_EXTENSIONS
        [SubclassSelector]
#endif
        private IResolver<bool> isWait = new SerializeField<bool>(true);

        [SerializeReference]
#if HK_FEEDBACK_V2_SERIALIZE_REFERENCE_EXTENSIONS
        [SubclassSelector]
#endif
        private IAction[] actions = null!;

        public IAction[] Actions => actions;

        public UniTask InvokeAsync(IContainer container, CancellationToken cancellationToken)
        {
            if (!isWait.Resolve(container))
            {
                actions.InvokeAsync(container, cancellationToken).Forget();
                return UniTask.CompletedTask;
            }
            else
            {
                return actions.InvokeAsync(container, cancellationToken);
            }
        }
    }
}
