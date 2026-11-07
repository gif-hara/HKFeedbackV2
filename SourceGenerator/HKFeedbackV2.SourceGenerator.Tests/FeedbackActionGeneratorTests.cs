#nullable enable
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using HKFeedbackV2.Resolvers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace HKFeedbackV2.SourceGenerator.Tests
{
    public sealed class FeedbackActionGeneratorTests
    {
        private static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.CSharp10);
        private static readonly ImmutableArray<MetadataReference> References =
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                .Append(typeof(IAction).Assembly.Location)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => MetadataReference.CreateFromFile(path))
                .ToImmutableArray<MetadataReference>();

        [Fact]
        public async Task StaticActionResolvesArgumentsAndInjectsExecutionContext()
        {
            var assembly = Compile("""
                using System.Threading;
                using HKFeedbackV2;
                namespace Game;
                public static class Effects
                {
                    public static int Value;
                    public static string Message;
                    public static IContainer Context;
                    public static CancellationToken Token;
                    [FeedbackAction]
                    public static void Run(int value = 3, string message = "hello",
                        IContainer context = null, CancellationToken token = default)
                    {
                        Value = value; Message = message; Context = context; Token = token;
                    }
                }
                """);
            var action = CreateAction(assembly, "Game.Effects_RunAction");
            var container = new Container();
            using var cancellation = new CancellationTokenSource();
            SetResolver(action, "_argument0", new Constant<int>(42));

            await action.InvokeAsync(container, cancellation.Token);

            var effects = assembly.GetType("Game.Effects")!;
            Assert.Equal(42, effects.GetField("Value")!.GetValue(null));
            Assert.Equal("hello", effects.GetField("Message")!.GetValue(null));
            Assert.Same(container, effects.GetField("Context")!.GetValue(null));
            Assert.Equal(cancellation.Token, effects.GetField("Token")!.GetValue(null));
            Assert.Equal(2, action.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance).Length);
            Assert.True(action.GetType().IsDefined(typeof(SerializableAttribute)));
        }

        [Fact]
        public async Task InstanceActionResolvesTargetFromAnonymousContainer()
        {
            var assembly = Compile("""
                using HKFeedbackV2;
                namespace Game;
                public sealed class Effects
                {
                    public int Value;
                    [FeedbackAction]
                    internal void Add(int value = 7) { Value += value; }
                }
                """);
            var target = Activator.CreateInstance(assembly.GetType("Game.Effects")!)!;
            var container = new Container();
            container.Register(ContainerKey.Anonymous, target);
            var action = CreateAction(assembly, "Game.Effects_AddAction");

            await action.InvokeAsync(container, default);

            Assert.Equal(7, target.GetType().GetField("Value")!.GetValue(target));
        }

        [Fact]
        public async Task InstanceTargetResolverCanBeReplaced()
        {
            var assembly = Compile("""
                using HKFeedbackV2;
                public class Effects
                {
                    public int Count;
                    [FeedbackAction] public void Run() { Count++; }
                }
                """);
            var owner = assembly.GetType("Effects")!;
            var target = Activator.CreateInstance(owner)!;
            var resolver = Activator.CreateInstance(typeof(Constant<>).MakeGenericType(owner), target)!;
            var action = CreateAction(assembly, "Effects_RunAction");
            SetResolver(action, "_target", resolver);

            await action.InvokeAsync(new Container(), default);

            Assert.Equal(1, owner.GetField("Count")!.GetValue(target));
        }

        [Theory]
        [InlineData("void", "return;", "")]
        [InlineData("UniTask", "return UniTask.CompletedTask;", "using Cysharp.Threading.Tasks;")]
        [InlineData("Task", "return Task.CompletedTask;", "using System.Threading.Tasks;")]
        public void CancellationPreventsInvocation(string returnType, string body, string import)
        {
            var assembly = Compile($$"""
                using HKFeedbackV2;
                {{import}}
                public static class Effects
                {
                    public static bool Called;
                    [FeedbackAction] public static {{returnType}} Run() { Called = true; {{body}} }
                }
                """);
            var action = CreateAction(assembly, "Effects_RunAction");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(() => action.InvokeAsync(new Container(), cancellation.Token));
            Assert.Equal(false, assembly.GetType("Effects")!.GetField("Called")!.GetValue(null));
        }

        [Theory]
        [InlineData("UniTask", "new UniTask(Pending.Task)")]
        [InlineData("Task", "Pending.Task")]
        public async Task AsyncActionPreservesCompletionAndExceptions(string returnType, string expression)
        {
            var assembly = Compile($$"""
                using HKFeedbackV2;
                using System.Threading.Tasks;
                using Cysharp.Threading.Tasks;
                public static class Effects
                {
                    public static TaskCompletionSource<bool> Pending = new TaskCompletionSource<bool>();
                    [FeedbackAction] public static {{returnType}} Run() => {{expression}};
                }
                """);
            var action = CreateAction(assembly, "Effects_RunAction");
            var pending = (TaskCompletionSource<bool>)assembly.GetType("Effects")!.GetField("Pending")!.GetValue(null)!;
            var result = action.InvokeAsync(new Container(), default).AsTask();
            Assert.False(result.IsCompleted);

            var error = new InvalidOperationException("action failed");
            pending.SetException(error);

            Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() => result));
        }

        [Fact]
        public void DefaultConstantsAndComplexTypesProduceCompilableCode()
        {
            Compile("""
                using HKFeedbackV2;
                using System;
                using System.Collections.Generic;
                public static class Effects
                {
                    [FeedbackAction] public static void Run(
                        string text = "line\n\"quoted\"", char character = '\'',
                        DayOfWeek day = DayOfWeek.Friday, float nan = float.NaN,
                        double infinity = double.PositiveInfinity, decimal amount = 1.5m,
                        bool enabled = true, int? optional = null, int[] values = null,
                        Dictionary<string, List<int>> map = null) { }
                }
                """);
        }

        [Fact]
        public void SupportsAliasesNestedTypesAndKeywords()
        {
            var generation = Run("""
                using Mark = HKFeedbackV2.FeedbackActionAttribute;
                namespace @event;
                internal class Outer
                {
                    internal class Inner
                    {
                        [Mark] internal static void @switch(int @event) { }
                        [Mark] public static void RunString(string value) { }
                        [Mark] public static void RunNumber(int value) { }
                    }
                }
                """);
            AssertSuccess(generation);
            Assert.Equal(3, generation.Result.Results.Single().GeneratedSources.Length);
            var code = string.Join("\n", generation.Result.Results.Single().GeneratedSources.Select(s => s.SourceText));
            Assert.Contains("Outer_Inner_switchAction", code);
            Assert.Contains("Outer_Inner_RunStringAction", code);
            Assert.Contains("Outer_Inner_RunNumberAction", code);
            Assert.Contains(".@switch(", code);
        }

        [Theory]
        [InlineData("[FeedbackAction] private static void Run() { }", "HKFSG002")]
        [InlineData("[FeedbackAction] public static void Run<T>() { }", "HKFSG002")]
        [InlineData("[FeedbackAction] public abstract void Run();", "HKFSG002")]
        [InlineData("[FeedbackAction] public static int Run() => 1;", "HKFSG003")]
        [InlineData("[FeedbackAction] public static System.Threading.Tasks.Task<int> Run() => null;", "HKFSG003")]
        [InlineData("[FeedbackAction] public static void Run(ref int value) { }", "HKFSG005")]
        [InlineData("[FeedbackAction] public static void Run(in int value) { }", "HKFSG005")]
        [InlineData("[FeedbackAction] public static void Run(out int value) { value = 0; }", "HKFSG005")]
        [InlineData("[FeedbackAction] public static void Run(System.Span<int> value) { }", "HKFSG005")]
        [InlineData("[FeedbackAction] public static unsafe void Run(int* value) { }", "HKFSG005")]
        [InlineData("[FeedbackAction] public static async void Run() { await System.Threading.Tasks.Task.Yield(); }", "HKFSG006")]
        public void InvalidDeclarationsReportActionableDiagnostics(string declaration, string diagnosticId)
        {
            var generation = Run("using HKFeedbackV2; public abstract class Effects { " + declaration + " }");
            var diagnostic = Assert.Single(generation.Result.Diagnostics);
            Assert.Equal(diagnosticId, diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.True(diagnostic.Location.IsInSource);
            Assert.Empty(generation.Result.Results.Single().GeneratedSources);
        }

        [Theory]
        [InlineData("public class Effects<T> { [FeedbackAction] public static void Run() { } }")]
        [InlineData("public class Outer { private class Effects { [FeedbackAction] public static void Run() { } } }")]
        [InlineData("public struct Effects { [FeedbackAction] public void Run() { } }")]
        public void UnsupportedContainingTypesAreDiagnosed(string declaration)
        {
            var generation = Run("using HKFeedbackV2; " + declaration);
            Assert.Equal("HKFSG002", Assert.Single(generation.Result.Diagnostics).Id);
        }

        [Fact]
        public void OverloadedMethodsReportNameCollisions()
        {
            var generation = Run("""
                using HKFeedbackV2;
                public class Effects
                {
                    [FeedbackAction] public static void Run(int value) { }
                    [FeedbackAction] public static void Run(string value) { }
                }
                """);
            Assert.Equal(2, generation.Result.Diagnostics.Length);
            Assert.All(generation.Result.Diagnostics, d => Assert.Equal("HKFSG004", d.Id));
            Assert.Empty(generation.Result.Results.Single().GeneratedSources);
        }

        [Fact]
        public void ExistingClassNameIsDiagnosed()
        {
            var generation = Run("""
                using HKFeedbackV2;
                public class Effects_RunAction { }
                public class Effects { [FeedbackAction] public static void Run() { } }
                """);
            Assert.Equal("HKFSG004", Assert.Single(generation.Result.Diagnostics).Id);
        }

        [Fact]
        public void ImplementedPartialMethodGeneratesOnlyOneAction()
        {
            var generation = Run("""
                using HKFeedbackV2;
                public partial class Effects
                {
                    [FeedbackAction] public static partial void Run();
                }
                public partial class Effects
                {
                    public static partial void Run() { }
                }
                """);
            AssertSuccess(generation);
            Assert.Single(generation.Result.Results.Single().GeneratedSources);
        }

        [Fact]
        public void UnrelatedAttributesAndUnannotatedMethodsAreIgnored()
        {
            var generation = Run("""
                using System;
                namespace Other;
                public class FeedbackActionAttribute : Attribute { }
                public class Effects
                {
                    [FeedbackAction] public static void Run() { }
                    public static void Unannotated() { }
                }
                """);
            AssertSuccess(generation);
            Assert.Empty(generation.Result.Results.Single().GeneratedSources);
        }

        private static Generation Run(string source)
        {
            var compilation = CSharpCompilation.Create("GeneratedActions_" + Guid.NewGuid().ToString("N"),
                new[] { CSharpSyntaxTree.ParseText(source, ParseOptions) }, References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new[] { new FeedbackActionGenerator().AsSourceGenerator() }, parseOptions: ParseOptions);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
            return new Generation(output, driver.GetRunResult(), diagnostics);
        }

        private static void AssertSuccess(Generation generation)
        {
            Assert.Empty(generation.Diagnostics);
            Assert.Empty(generation.Result.Diagnostics);
            var errors = generation.Compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error);
            Assert.True(!errors.Any(), string.Join(Environment.NewLine, errors));
        }

        private static Assembly Compile(string source)
        {
            var generation = Run(source);
            AssertSuccess(generation);
            using var stream = new MemoryStream();
            var result = generation.Compilation.Emit(stream);
            Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
            stream.Position = 0;
            return AssemblyLoadContext.Default.LoadFromStream(stream);
        }

        private static IAction CreateAction(Assembly assembly, string name) =>
            (IAction)Activator.CreateInstance(assembly.GetType(name, throwOnError: true)!)!;

        private static void SetResolver(IAction action, string field, object resolver) =>
            action.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(action, resolver);

        private sealed record Generation(Compilation Compilation, GeneratorDriverRunResult Result,
            ImmutableArray<Diagnostic> Diagnostics);
    }
}
