using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using HKFeedbackV2.Evaluators;
using HKFeedbackV2.Resolvers;
using UnityEngine;

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
        public static UniTask AnotherActionAsync(IContainer container, IAction[] actions, CancellationToken cancellationToken)
        {
            return actions.InvokeAsync(container, cancellationToken);
        }

        [Serializable]
        public sealed class SelectorElement
        {
            [field: SerializeReference]
#if HK_FEEDBACK_V2_SERIALIZE_REFERENCE_EXTENSIONS
            [field: SubclassSelector]
#endif
            public IEvaluator Condition { get; private set; } = new Constant(true);

            [field: SerializeReference]
#if HK_FEEDBACK_V2_SERIALIZE_REFERENCE_EXTENSIONS
            [field: SubclassSelector]
#endif
            public IResolver<IAction[]> Actions { get; private set; } = new SerializeReference<IAction[]>(Array.Empty<IAction>());
        }

        [FeedbackAction]
        public static async UniTask SelectorAsync(IContainer container, SelectorElement[] elements, CancellationToken cancellationToken)
        {
            foreach (var element in elements)
            {
                if (element.Condition.Evaluate(container))
                {
                    await element.Actions.Resolve(container).InvokeAsync(container, cancellationToken);
                    return;
                }
            }
        }
    }
}
