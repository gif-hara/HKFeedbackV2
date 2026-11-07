using System.Threading;
using Cysharp.Threading.Tasks;

namespace HKFeedbackV2
{
    public static class Extensions
    {
        public static async UniTask InvokeAsync(this IAction[] feedbacks, IContainer container, CancellationToken cancellationToken)
        {
            foreach (var feedback in feedbacks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await feedback.InvokeAsync(container, cancellationToken);
            }
        }
    }
}
