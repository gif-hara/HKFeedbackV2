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
                public static partial class Effects
                {
                    public static int Value;
                    public static string Message;
                    public static IContainer Context;
                    public static CancellationToken Token;
                    public static IAction CreateAction() => new Effects.Actions.Run_int_string_IContainer_CancellationToken();
                    [FeedbackAction]
                    public static void Run(int value = 3, string message = "hello",
                        IContainer context = null, CancellationToken token = default)
                    {
                        Value = value; Message = message; Context = context; Token = token;
                    }
                }
                """);
            var action = CreateAction(assembly, "Game.Effects+Actions+Run_int_string_IContainer_CancellationToken");
            var container = new Container();
            using var cancellation = new CancellationTokenSource();
            SetResolver(action, "_argument0", new Embed<int>(42));

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
                public sealed partial class Effects
                {
                    public int Value;
                    [FeedbackAction]
                    private void Add(int value = 7) { Value += value; }
                }
                """);
            var target = Activator.CreateInstance(assembly.GetType("Game.Effects")!)!;
            var container = new Container();
            container.Register(ContainerKey.Anonymous, target);
            var action = CreateAction(assembly, "Game.Effects+Actions+Add_int");

            await action.InvokeAsync(container, default);

            Assert.Equal(7, target.GetType().GetField("Value")!.GetValue(target));
        }

        [Fact]
        public async Task InstanceTargetResolverCanBeReplaced()
        {
            var assembly = Compile("""
                using HKFeedbackV2;
                public partial class Effects
                {
                    public int Count;
                    [FeedbackAction] public void Run() { Count++; }
                }
                """);
            var owner = assembly.GetType("Effects")!;
            var target = Activator.CreateInstance(owner)!;
            var resolver = Activator.CreateInstance(typeof(Embed<>).MakeGenericType(owner), target)!;
            var action = CreateAction(assembly, "Effects+Actions+Run");
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
                public static partial class Effects
                {
                    public static bool Called;
                    [FeedbackAction] public static {{returnType}} Run() { Called = true; {{body}} }
                }
                """);
            var action = CreateAction(assembly, "Effects+Actions+Run");
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
                public static partial class Effects
                {
                    public static TaskCompletionSource<bool> Pending = new TaskCompletionSource<bool>();
                    [FeedbackAction] public static {{returnType}} Run() => {{expression}};
                }
                """);
            var action = CreateAction(assembly, "Effects+Actions+Run");
            var pending = (TaskCompletionSource<bool>)assembly.GetType("Effects")!.GetField("Pending")!.GetValue(null)!;
            Assert.Equal(returnType == "UniTask" ? typeof(HKFeedbackV2.Actions.AsyncAction) : typeof(object),
                action.GetType().BaseType);
            var result = action.InvokeAsync(new Container(), default).AsTask();
            Assert.False(result.IsCompleted);

            var error = new InvalidOperationException("action failed");
            pending.SetException(error);

            Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() => result));
        }

        [Fact]
        public async Task UniTaskActionCanRunWithoutWaiting()
        {
            var assembly = Compile("""
                using HKFeedbackV2;
                using System.Threading;
                using System.Threading.Tasks;
                using Cysharp.Threading.Tasks;
                public static partial class Effects
                {
                    public static TaskCompletionSource<bool> Pending = new TaskCompletionSource<bool>();
                    public static IContainer Context;
                    public static CancellationToken Token;
                    [FeedbackAction] private static UniTask Run(IContainer context, CancellationToken token)
                    {
                        Context = context;
                        Token = token;
                        return new UniTask(Pending.Task);
                    }
                }
                """);
            var action = CreateAction(assembly, "Effects+Actions+Run_IContainer_CancellationToken");
            Assert.IsAssignableFrom<HKFeedbackV2.Actions.AsyncAction>(action);
            typeof(HKFeedbackV2.Actions.AsyncAction).GetField("isWait", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(action, new Embed<bool>(false));
            var container = new Container();
            using var cancellation = new CancellationTokenSource();
            var result = action.InvokeAsync(container, cancellation.Token).AsTask();
            var effects = assembly.GetType("Effects")!;
            var pending = (TaskCompletionSource<bool>)effects.GetField("Pending")!.GetValue(null)!;

            Assert.True(result.IsCompletedSuccessfully);
            Assert.False(pending.Task.IsCompleted);
            Assert.Same(container, effects.GetField("Context")!.GetValue(null));
            Assert.Equal(cancellation.Token, effects.GetField("Token")!.GetValue(null));
            pending.SetResult(true);
            await pending.Task;
        }

        [Theory]
        [InlineData("Run")]
        [InlineData("InvokeAsync")]
        [InlineData("InvokeAsyncInternal")]
        public async Task UniTaskInstanceActionResolvesTargetAndArguments(string methodName)
        {
            var assembly = Compile($$"""
                using HKFeedbackV2;
                using System.Threading;
                using Cysharp.Threading.Tasks;
                public partial class Effects
                {
                    public int Count;
                    public CancellationToken Token;
                    [FeedbackAction] private UniTask {{methodName}}(int value = 5, CancellationToken token = default)
                    {
                        Count += value;
                        Token = token;
                        return UniTask.CompletedTask;
                    }
                }
                """);
            var owner = assembly.GetType("Effects")!;
            var target = Activator.CreateInstance(owner)!;
            var container = new Container();
            container.Register(ContainerKey.Anonymous, target);
            var action = CreateAction(assembly, "Effects+Actions+" + methodName + "_int_CancellationToken");
            Assert.IsAssignableFrom<HKFeedbackV2.Actions.AsyncAction>(action);
            SetResolver(action, "_argument0", new Embed<int>(17));
            using var cancellation = new CancellationTokenSource();

            await action.InvokeAsync(container, cancellation.Token);

            Assert.Equal(17, owner.GetField("Count")!.GetValue(target));
            Assert.Equal(cancellation.Token, owner.GetField("Token")!.GetValue(target));
        }

        [Fact]
        public void UniTaskMethodNamedLikeRequiredOverrideIsDiagnosed()
        {
            var generation = Run("""
                using HKFeedbackV2;
                using Cysharp.Threading.Tasks;
                public partial class Effects
                {
                    [FeedbackAction] private UniTask InvokeAsyncInternal() => UniTask.CompletedTask;
                }
                """);
            Assert.Equal("HKFSG004", Assert.Single(generation.Result.Diagnostics).Id);
            Assert.Empty(generation.Result.Results.Single().GeneratedSources);
        }

        [Fact]
        public void DefaultConstantsAndComplexTypesProduceCompilableCode()
        {
            Compile("""
                using HKFeedbackV2;
                using System;
                using System.Collections.Generic;
                public static partial class Effects
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
                internal partial class Outer
                {
                    internal partial class Inner
                    {
                        [Mark] internal static void @switch(int @event) { }
                        [Mark] public static void RunString(string value) { }
                        [Mark] public static void RunNumber(int value) { }
                    }
                }
                """);
            AssertSuccess(generation);
            Assert.Single(generation.Result.Results.Single().GeneratedSources);
            var code = string.Join("\n", generation.Result.Results.Single().GeneratedSources.Select(s => s.SourceText));
            Assert.Contains("class @switch_int :", code);
            Assert.Contains("class @RunString_string :", code);
            Assert.Contains("class @RunNumber_int :", code);
            Assert.Contains(".@switch(", code);
        }

        [Theory]
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
            var generation = Run("using HKFeedbackV2; public abstract partial class Effects { " + declaration + " }");
            var diagnostic = Assert.Single(generation.Result.Diagnostics);
            Assert.Equal(diagnosticId, diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.True(diagnostic.Location.IsInSource);
            Assert.Empty(generation.Result.Results.Single().GeneratedSources);
        }

        [Theory]
        [InlineData("public partial class Effects<T> { [FeedbackAction] public static void Run() { } }")]
        [InlineData("public partial class Outer<T> { private partial class Effects { [FeedbackAction] public static void Run() { } } }")]
        [InlineData("public struct Effects { [FeedbackAction] public void Run() { } }")]
        public void UnsupportedContainingTypesAreDiagnosed(string declaration)
        {
            var generation = Run("using HKFeedbackV2; " + declaration);
            Assert.Equal("HKFSG002", Assert.Single(generation.Result.Diagnostics).Id);
        }

        [Fact]
        public async Task OverloadedMethodsGenerateAndInvokeEachSignature()
        {
            var assembly = Compile("""
                using HKFeedbackV2;
                using System.Threading;
                using Cysharp.Threading.Tasks;
                public partial class Effects
                {
                    public static int Value;
                    public static CancellationToken Token;
                    [FeedbackAction] private static void Play() { Value = 0; }
                    [FeedbackAction] private static void Play(int value = 7) { Value = value; }
                    [FeedbackAction] private static void Play(string value = "hello") { Value = value.Length; }
                    [FeedbackAction] private static void Play(int value = 4, float scale = 2) { Value = (int)(value * scale); }
                    [FeedbackAction] private static void Play(float scale = 3, int value = 4) { Value = (int)(value + scale); }
                    [FeedbackAction] private static UniTask Play(int value = 1, CancellationToken token = default)
                    {
                        Value = 100 + value;
                        Token = token;
                        return UniTask.CompletedTask;
                    }
                    public static IAction[] CreateActions() => new IAction[]
                    {
                        new Actions.Play(), new Actions.Play_int(), new Actions.Play_string(),
                        new Actions.Play_int_float(), new Actions.Play_float_int(), new Actions.Play_int_CancellationToken()
                    };
                }
                """);
            var owner = assembly.GetType("Effects")!;
            var actions = (IAction[])owner.GetMethod("CreateActions")!.Invoke(null, null)!;
            var expectedNames = new[] { "Play", "Play_int", "Play_string", "Play_int_float", "Play_float_int", "Play_int_CancellationToken" };
            var expectedValues = new[] { 0, 7, 5, 8, 7, 101 };
            using var cancellation = new CancellationTokenSource();
            for (var i = 0; i < actions.Length; i++)
            {
                Assert.Equal(expectedNames[i], actions[i].GetType().Name);
                await actions[i].InvokeAsync(new Container(), cancellation.Token);
                Assert.Equal(expectedValues[i], owner.GetField("Value")!.GetValue(null));
                Assert.Equal(i == actions.Length - 1, actions[i] is HKFeedbackV2.Actions.AsyncAction);
            }
            Assert.Equal(cancellation.Token, owner.GetField("Token")!.GetValue(null));
        }

        [Fact]
        public void NormalizedActionNameCollisionsAreDiagnosed()
        {
            var generation = Run("""
                using HKFeedbackV2;
                public partial class Effects
                {
                    [FeedbackAction] public static void Play(int value) { }
                    [FeedbackAction] public static void Play_int() { }
                }
                """);
            Assert.Equal(2, generation.Result.Diagnostics.Length);
            Assert.All(generation.Result.Diagnostics, d => Assert.Equal("HKFSG004", d.Id));
            Assert.Empty(generation.Result.Results.Single().GeneratedSources);
        }

        [Theory]
        [InlineData("System.Int32", "int")]
        [InlineData("global::System.Single", "float")]
        [InlineData("string", "string")]
        [InlineData("int[]", "int_array")]
        [InlineData("int[,]", "int_array2")]
        [InlineData("int?", "int_nullable")]
        [InlineData("System.Collections.Generic.List<int>", "List_int")]
        [InlineData("System.Collections.Generic.Dictionary<string, int[]>", "Dictionary_string_int_array")]
        [InlineData("dynamic", "object")]
        public void ParameterTypesProduceValidReadableClassNames(string parameterType, string suffix)
        {
            var assembly = Compile($$"""
                using HKFeedbackV2;
                public static partial class Effects
                {
                    [FeedbackAction] private static void Play({{parameterType}} value) { }
                }
                """);
            Assert.NotNull(assembly.GetType("Effects+Actions+Play_" + suffix));
        }

        [Fact]
        public void AddingOverloadsAndRenamingParametersKeepExistingActionNames()
        {
            var single = Compile("""
                using HKFeedbackV2;
                public static partial class Effects
                {
                    [FeedbackAction] public static void Play(int count) { }
                }
                """);
            var multiple = Compile("""
                using HKFeedbackV2;
                public static partial class Effects
                {
                    [FeedbackAction] public static void Play(string message) { }
                    [FeedbackAction] public static void Play(int renamed) { }
                }
                """);
            Assert.NotNull(single.GetType("Effects+Actions+Play_int"));
            Assert.NotNull(multiple.GetType("Effects+Actions+Play_int"));
            Assert.NotNull(multiple.GetType("Effects+Actions+Play_string"));
        }

        [Fact]
        public async Task DynamicParameterInvokesItsDeclaredOverload()
        {
            var assembly = Compile("""
                using HKFeedbackV2;
                public static partial class Effects
                {
                    public static string Last;
                    [FeedbackAction] private static void Play(dynamic value) { Last = "object"; }
                    [FeedbackAction] private static void Play(int value) { Last = "int"; }
                }
                """);
            var action = CreateAction(assembly, "Effects+Actions+Play_object");
            SetResolver(action, "_argument0", new Embed<object>(42));
            await action.InvokeAsync(new Container(), default);
            Assert.Equal("object", assembly.GetType("Effects")!.GetField("Last")!.GetValue(null));
        }

        [Fact]
        public void ExistingClassNameIsDiagnosed()
        {
            var generation = Run("""
                using HKFeedbackV2;
                public partial class Effects
                {
                    public static class Actions { }
                    [FeedbackAction] public static void Run() { }
                }
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
        public async Task DifferentOwnersHaveTheirOwnActionsContainers()
        {
            var assembly = Compile("""
                using HKFeedbackV2;
                public static partial class First
                {
                    public static int Count;
                    [FeedbackAction] private static void Run() { Count++; }
                }
                public static partial class Second
                {
                    public static int Count;
                    [FeedbackAction] private static void Run() { Count++; }
                }
                public static class Consumer
                {
                    public static IAction CreateFirst() => new First.Actions.Run();
                    public static IAction CreateSecond() => new Second.Actions.Run();
                }
                """);
            await CreateAction(assembly, "First+Actions+Run").InvokeAsync(new Container(), default);
            await CreateAction(assembly, "Second+Actions+Run").InvokeAsync(new Container(), default);
            Assert.Equal(1, assembly.GetType("First")!.GetField("Count")!.GetValue(null));
            Assert.Equal(1, assembly.GetType("Second")!.GetField("Count")!.GetValue(null));
        }

        [Fact]
        public async Task PrivateNestedClassCanGenerateActions()
        {
            var assembly = Compile("""
                using HKFeedbackV2;
                public partial class Outer
                {
                    public static bool Called;
                    public static IAction Create() => new Inner.Actions.Run();
                    private sealed partial class Inner
                    {
                        [FeedbackAction] private static void Run() { Called = true; }
                    }
                }
                """);
            var action = (IAction)assembly.GetType("Outer")!.GetMethod("Create")!.Invoke(null, null)!;
            await action.InvokeAsync(new Container(), default);
            Assert.Equal(true, assembly.GetType("Outer")!.GetField("Called")!.GetValue(null));
        }

        [Theory]
        [InlineData("public class Effects { [FeedbackAction] public static void Run() { } }")]
        [InlineData("public class Outer { private partial class Inner { [FeedbackAction] private static void Run() { } } }")]
        [InlineData("public partial class Effects { [FeedbackAction] public static void Run() { } } public class Effects { }")]
        public void NonPartialContainingClassesAreDiagnosed(string declaration)
        {
            var generation = Run("using HKFeedbackV2; " + declaration);
            Assert.Equal("HKFSG007", Assert.Single(generation.Result.Diagnostics).Id);
            Assert.Empty(generation.Result.Results.Single().GeneratedSources);
        }

        [Theory]
        [InlineData("public int Actions;")]
        [InlineData("public void Actions() { }")]
        public void ExistingActionsMembersAreDiagnosed(string member)
        {
            var generation = Run("using HKFeedbackV2; public partial class Effects { " + member +
                " [FeedbackAction] private void Run() { } }");
            Assert.Equal("HKFSG004", Assert.Single(generation.Result.Diagnostics).Id);
        }

        [Theory]
        [InlineData("public partial class Actions { [FeedbackAction] public static void Run() { } }")]
        [InlineData("public partial class Effects { [FeedbackAction] public static void Actions() { } }")]
        public void NamesConflictingWithTheActionsContainerAreDiagnosed(string declaration)
        {
            var generation = Run("using HKFeedbackV2; " + declaration);
            Assert.Equal("HKFSG004", Assert.Single(generation.Result.Diagnostics).Id);
        }

        [Theory]
        [InlineData("InvokeAsync")]
        [InlineData("_argument0")]
        [InlineData("_target")]
        public async Task MethodNamesCanMatchGeneratedMembers(string methodName)
        {
            var assembly = Compile($$"""
                using HKFeedbackV2;
                public partial class Effects
                {
                    public int Count;
                    [FeedbackAction] private void {{methodName}}(int value = 5) { Count += value; }
                }
                """);
            var owner = assembly.GetType("Effects")!;
            var target = Activator.CreateInstance(owner)!;
            var container = new Container();
            container.Register(ContainerKey.Anonymous, target);
            await CreateAction(assembly, "Effects+Actions+" + methodName + "_int").InvokeAsync(container, default);
            Assert.Equal(5, owner.GetField("Count")!.GetValue(target));
        }

        [Theory]
        [InlineData("InvokeAsync")]
        [InlineData("_target")]
        public async Task ParameterlessMethodNamesCanMatchGeneratedMembers(string methodName)
        {
            var assembly = Compile($$"""
                using HKFeedbackV2;
                public partial class Effects
                {
                    public int Count;
                    [FeedbackAction] private void {{methodName}}() { Count++; }
                }
                """);
            var owner = assembly.GetType("Effects")!;
            var target = Activator.CreateInstance(owner)!;
            var container = new Container();
            container.Register(ContainerKey.Anonymous, target);
            await CreateAction(assembly, "Effects+Actions+" + methodName).InvokeAsync(container, default);
            Assert.Equal(1, owner.GetField("Count")!.GetValue(target));
        }

        [Fact]
        public void UnrelatedAttributesAndUnannotatedMethodsAreIgnored()
        {
            var generation = Run("""
                using System;
                namespace Other;
                public class FeedbackActionAttribute : Attribute { }
                public partial class Effects
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
