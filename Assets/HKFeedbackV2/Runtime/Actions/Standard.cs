using System;
using System.Buffers;
using System.Threading;
using Cysharp.Threading.Tasks;
using HKFeedbackV2.Evaluators;
using HKFeedbackV2.Resolvers;
using UnityEngine;

namespace HKFeedbackV2
{
    public static partial class Standard
    {
        [Serializable]
        public sealed class RegisterElement<T>
        {
            [field: SerializeReference]
#if HK_FEEDBACK_V2_SERIALIZE_REFERENCE_EXTENSIONS
            [field: SubclassSelector]
#endif
            public IResolver<ContainerKey> Key { get; private set; } = new SerializeField<ContainerKey>();

            [field: SerializeReference]
#if HK_FEEDBACK_V2_SERIALIZE_REFERENCE_EXTENSIONS
            [field: SubclassSelector]
#endif
            public IResolver<T> Target { get; private set; } = new SerializeField<T>();

            public RegisterElement() : this(null, default)
            {
            }

            public RegisterElement(ContainerKey key, T target)
            {
                Key = new SerializeField<ContainerKey>(key);
                Target = new SerializeField<T>(target);
            }
        }

        private static void RegisterTargets<T>(IContainer container, RegisterElement<T>[] elements)
        {
            if (elements == null)
            {
                throw new ArgumentNullException(nameof(elements));
            }
            if (elements.Length == 0)
            {
                return;
            }

            // Resolve the whole batch before writing to the container.
            var pool = ArrayPool<(ContainerKey Key, T Target)>.Shared;
            var entries = pool.Rent(elements.Length);
            try
            {
                var index = 0;
                foreach (var element in elements)
                {
                    if (element?.Key == null || element.Target == null)
                    {
                        throw new ArgumentException("Every register element must have a key and target resolver.", nameof(elements));
                    }

                    var key = element.Key.Resolve(container);
                    if (key == null)
                    {
                        throw new ArgumentException("Every register element must resolve to a key.", nameof(elements));
                    }
                    entries[index++] = (key, element.Target.Resolve(container));
                }

                foreach (var entry in entries.AsSpan(0, elements.Length))
                {
                    container.Register(entry.Key, entry.Target);
                }
            }
            finally
            {
                pool.Return(entries, clearArray: true);
            }
        }

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
