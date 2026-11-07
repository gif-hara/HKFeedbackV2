#nullable enable
// Minimal Unity/UniTask stand-ins let the real HKFeedbackV2 runtime sources run
// under dotnet test. The generator is also verified separately in Unity.
using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeReference : Attribute { }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute { }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class InspectorName : Attribute
    {
        public InspectorName(string displayName) { }
    }
}

[AttributeUsage(AttributeTargets.Field)]
public sealed class SubclassSelectorAttribute : Attribute { }

namespace Cysharp.Threading.Tasks
{
    public readonly struct UniTask
    {
        private readonly Task? task;
        public UniTask(Task task) => this.task = task;
        public static UniTask CompletedTask => new UniTask(Task.CompletedTask);
        public Task AsTask() => task ?? Task.CompletedTask;
        public TaskAwaiter GetAwaiter() => AsTask().GetAwaiter();
    }

    public static class UniTaskExtensions
    {
        public static UniTask AsUniTask(Task task) => new UniTask(task);

        public static void Forget(this UniTask task)
        {
            // The stand-in already holds a running Task; observe failures without waiting.
            _ = task.AsTask().ContinueWith(completed => { _ = completed.Exception; },
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        }
    }
}

namespace HKFeedbackV2
{
    public sealed class ContainerKey
    {
        public static ContainerKey Anonymous { get; } = new ContainerKey();
    }
}
