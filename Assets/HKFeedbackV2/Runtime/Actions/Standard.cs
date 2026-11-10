using System.Threading;
using Cysharp.Threading.Tasks;

namespace HKFeedbackV2
{
    public static partial class Standard
    {
        [FeedbackAction]
        public static async UniTask WhileAsync(IContainer container, IEvaluator condition, IAction[] actions, CancellationToken cancellationToken)
        {
            while (condition.Evaluate(container) && !cancellationToken.IsCancellationRequested)
            {
                await actions.InvokeAsync(container, cancellationToken);
            }
        }

        [FeedbackAction]
        public static UniTask EvaluateActionAsync(IContainer container, IEvaluator condition, IAction[] actions, CancellationToken cancellationToken)
        {
            if (condition.Evaluate(container))
            {
                return actions.InvokeAsync(container, cancellationToken);
            }
            else
            {
                return UniTask.CompletedTask;
            }
        }

        [FeedbackAction]
        public static async UniTask YieldAsync(PlayerLoopTiming timing = PlayerLoopTiming.Update, CancellationToken cancellationToken = default)
        {
            await UniTask.Yield(timing, cancellationToken);
        }

        [FeedbackAction]
        public static UniTask DelayAsync(float seconds, bool ignoreTimeScale = false, PlayerLoopTiming timing = PlayerLoopTiming.Update, CancellationToken cancellationToken = default, bool cancelImmediately = false)
        {
            return UniTask.Delay(System.TimeSpan.FromSeconds(seconds), ignoreTimeScale, timing, cancellationToken, cancelImmediately);
        }

        [FeedbackAction]
        public static UniTask WaitUntilAsync(IContainer container, IEvaluator condition, PlayerLoopTiming timing = PlayerLoopTiming.Update, CancellationToken cancellationToken = default)
        {
            return UniTask.WaitUntil((container, condition), static (state) => state.condition.Evaluate(state.container), timing, cancellationToken);
        }

        [FeedbackAction]
        public static UniTask BranchAsync(IContainer container, IEvaluator condition, IAction[] trueActions, IAction[] falseActions, CancellationToken cancellationToken)
        {
            if (condition.Evaluate(container))
            {
                return trueActions.InvokeAsync(container, cancellationToken);
            }
            else
            {
                return falseActions.InvokeAsync(container, cancellationToken);
            }
        }
    }
}
