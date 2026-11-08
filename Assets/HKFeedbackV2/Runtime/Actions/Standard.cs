using System.Threading;
using Cysharp.Threading.Tasks;

namespace HKFeedbackV2
{
    public partial class Standard
    {
        [FeedbackAction]
        public static async UniTask WhileAsync(IContainer container, IEvaluator condition, IAction[] actions, CancellationToken cancellationToken)
        {
            while (condition.Evaluate(container) && !cancellationToken.IsCancellationRequested)
            {
                await actions.InvokeAsync(container, cancellationToken);
            }
        }
    }
}
