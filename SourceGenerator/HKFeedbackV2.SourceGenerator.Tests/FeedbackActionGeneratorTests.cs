#nullable enable
using System;
using System.Collections.Generic;
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
                    public static IAction CreateAction() => new Effects.Actions.Run();
                    [FeedbackAction]
                    public static void Run(int value = 3, string message = "hello",
                        IContainer context = null, CancellationToken token = default)
                    {
                        Value = value; Message = message; Context = context; Token = token;
                    }
                }
                """);
            var action = CreateAction(assembly, "Game.Effects+Actions+Run");
            var container = new Container();
            using var cancellation = new CancellationTokenSource();
            SetResolver(action, "value", new SerializeField<int>(42));

            await action.InvokeAsync(container, cancellation.Token);

            var effects = assembly.GetType("Game.Effects")!;
            Assert.Equal(42, effects.GetField("Value")!.GetValue(null));
            Assert.Equal("hello", effects.GetField("Message")!.GetValue(null));
            Assert.Same(container, effects.GetField("Context")!.GetValue(null));
            Assert.Equal(cancellation.Token, effects.GetField("Token")!.GetValue(null));
            Assert.Equal(2, action.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance).Length);
            Assert.Equal(new[] { "message", "value" }, action.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .Select(field => field.Name).OrderBy(name => name));
            Assert.True(action.GetType().IsDefined(typeof(SerializableAttribute)));
        }

        [Theory]
        [InlineData("void", "return;")]
        [InlineData("UniTask", "return UniTask.CompletedTask;")]
        [InlineData("Task", "return Task.CompletedTask;")]
        public async Task InterfaceArgumentsAndInterfaceArraysUseSerializeReference(string returnType, string returnStatement)
        {
            var assembly = Compile($$"""
                using HKFeedbackV2;
                using Cysharp.Threading.Tasks;
                using System.Threading;
                using System.Threading.Tasks;
                public sealed class Condition : IEvaluator
                {
                    public bool Result;
                    public bool Evaluate(IContainer container) => Result;
                }
                public static partial class Effects
                {
                    public static int Total;
                    public static IContainer Context;
                    public static CancellationToken Token;
                    [FeedbackAction]
                    private static {{returnType}} Run(IEvaluator condition, IEvaluator[] conditions, int amount = 3,
                        IContainer context = null, CancellationToken token = default)
                    {
                        Total = condition.Evaluate(context) ? amount : 0;
                        foreach (var item in conditions)
                            if (item.Evaluate(context)) Total += amount;
                        Context = context;
                        Token = token;
                        {{returnStatement}}
                    }
                    [FeedbackAction] private static void Plain(Condition condition, Condition[] conditions) { }
                }
                """);
            var action = CreateAction(assembly, "Effects+Actions+Run");
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var condition = Assert.IsType<SerializeReference<IEvaluator>>(action.GetType().GetField("condition", flags)!.GetValue(action));
            var conditions = Assert.IsType<SerializeReference<IEvaluator[]>>(action.GetType().GetField("conditions", flags)!.GetValue(action));
            Assert.IsType<SerializeField<int>>(action.GetType().GetField("amount", flags)!.GetValue(action));
            Assert.Null(action.GetType().GetField("context", flags));
            Assert.Null(action.GetType().GetField("token", flags));
            var implementation = assembly.GetType("Condition")!;
            var truth = (IEvaluator)Activator.CreateInstance(implementation)!;
            implementation.GetField("Result")!.SetValue(truth, true);
            var falsity = (IEvaluator)Activator.CreateInstance(implementation)!;
            condition.GetType().GetField("value", flags)!.SetValue(condition, truth);
            conditions.GetType().GetField("value", flags)!.SetValue(conditions, new[] { truth, falsity });
            var container = new Container();
            using var cancellation = new CancellationTokenSource();

            await action.InvokeAsync(container, cancellation.Token);

            var owner = assembly.GetType("Effects")!;
            Assert.Equal(6, owner.GetField("Total")!.GetValue(null));
            Assert.Same(container, owner.GetField("Context")!.GetValue(null));
            Assert.Equal(cancellation.Token, owner.GetField("Token")!.GetValue(null));
            var plain = CreateAction(assembly, "Effects+Actions+Plain");
            foreach (var field in plain.GetType().GetFields(flags))
                Assert.Equal(typeof(SerializeField<>), field.GetValue(plain)!.GetType().GetGenericTypeDefinition());
        }

        [Fact]
        public async Task PrivateInstanceActionUsesSerializeReferenceForInterfaceArguments()
        {
            var assembly = Compile("""
                using HKFeedbackV2;
                public partial class Effects
                {
                    public bool Result;
                    [FeedbackAction] private void Run(IEvaluator condition, IContainer context)
                    {
                        Result = condition.Evaluate(context);
                    }
                }
                public sealed class Condition : IEvaluator
                {
                    public bool Evaluate(IContainer container) => container != null;
                }
                """);
            var target = Activator.CreateInstance(assembly.GetType("Effects")!)!;
            var condition = (IEvaluator)Activator.CreateInstance(assembly.GetType("Condition")!)!;
            var container = new Container();
            container.Register(ContainerKey.Anonymous, target);
            var action = CreateAction(assembly, "Effects+Actions+Run");
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            Assert.Equal(typeof(Anonymous<>), action.GetType().GetField("_target", flags)!.GetValue(action)!.GetType().GetGenericTypeDefinition());
            Assert.IsType<SerializeReference<IEvaluator>>(action.GetType().GetField("condition", flags)!.GetValue(action));
            SetResolver(action, "condition", new SerializeReference<IEvaluator>(condition));

            await action.InvokeAsync(container, default);

            Assert.Equal(true, target.GetType().GetField("Result")!.GetValue(target));
        }

        [Fact]
        public void InterfaceArgumentSelectionAppliesToActions()
        {
            var assembly = FeedbackGeneratorTestDriver.Compile("""
                using HKFeedbackV2;
                public static partial class Effects
                {
                    [FeedbackAction] private static void Run(IEvaluator condition, IEvaluator[] conditions) { }
                    [FeedbackResolver, FeedbackEvaluator]
                    private static bool Check(IEvaluator condition, IEvaluator[] conditions) => true;
                }
                """);
            foreach (var (typeName, resolverType) in new[]
            {
                ("Effects+Actions+Run", typeof(SerializeReference<>)),
                ("Effects+Resolvers+Check", typeof(SerializeField<>)),
                ("Effects+Evaluators+Check", typeof(SerializeField<>))
            })
            {
                var instance = Activator.CreateInstance(assembly.GetType(typeName)!)!;
                foreach (var field in instance.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
                    Assert.Equal(resolverType, field.GetValue(instance)!.GetType().GetGenericTypeDefinition());
            }
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
            var action = CreateAction(assembly, "Game.Effects+Actions+Add");

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
            var resolver = Activator.CreateInstance(typeof(SerializeField<>).MakeGenericType(owner), target)!;
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
            var action = CreateAction(assembly, "Effects+Actions+Run");
            Assert.IsAssignableFrom<HKFeedbackV2.Actions.AsyncAction>(action);
            typeof(HKFeedbackV2.Actions.AsyncAction).GetField("isWait", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(action, new SerializeField<bool>(false));
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
            var action = CreateAction(assembly, "Effects+Actions+" + methodName);
            Assert.IsAssignableFrom<HKFeedbackV2.Actions.AsyncAction>(action);
            SetResolver(action, "value", new SerializeField<int>(17));
            using var cancellation = new CancellationTokenSource();

            await action.InvokeAsync(container, cancellation.Token);

            Assert.Equal(17, owner.GetField("Count")!.GetValue(target));
            Assert.Equal(cancellation.Token, owner.GetField("Token")!.GetValue(target));
        }

        [Theory]
        [InlineData("")]
        [InlineData("int value")]
        public void UniTaskMethodNamedLikeRequiredOverrideIsDiagnosed(string parameters)
        {
            var generation = Run($$"""
                using HKFeedbackV2;
                using Cysharp.Threading.Tasks;
                public partial class Effects
                {
                    [FeedbackAction] private UniTask InvokeAsyncInternal({{parameters}}) => UniTask.CompletedTask;
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
            Assert.Contains("class @switch :", code);
            Assert.Contains("class @RunString :", code);
            Assert.Contains("class @RunNumber :", code);
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
                    [FeedbackAction] public static void Play(string message) { }
                    [FeedbackAction] public static void Play_int() { }
                }
                """);
            Assert.Equal(2, generation.Result.Diagnostics.Length);
            Assert.All(generation.Result.Diagnostics, d => Assert.Equal("HKFSG004", d.Id));
            var source = Assert.Single(generation.Result.Results.Single().GeneratedSources).SourceText.ToString();
            Assert.Contains("class @Play_string :", source);
            Assert.DoesNotContain("class @Play_int :", source);
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
                    [FeedbackAction] private static void Play() { }
                    [FeedbackAction] private static void Play({{parameterType}} value) { }
                }
                """);
            Assert.NotNull(assembly.GetType("Effects+Actions+Play_" + suffix));
        }

        [Fact]
        public void OnlyOverloadedMethodsUseParameterTypesInClassNames()
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
            Assert.NotNull(single.GetType("Effects+Actions+Play"));
            Assert.Null(single.GetType("Effects+Actions+Play_int"));
            Assert.Null(multiple.GetType("Effects+Actions+Play"));
            Assert.NotNull(multiple.GetType("Effects+Actions+Play_int"));
            Assert.NotNull(multiple.GetType("Effects+Actions+Play_string"));
        }

        [Fact]
        public void UnannotatedOverloadsAndOtherOwnersDoNotChangeActionNames()
        {
            var assembly = Compile("""
                using HKFeedbackV2;
                public partial class First
                {
                    [FeedbackAction] public static void Play(int value) { }
                    public static void Play(string message) { }
                }
                public partial class Second
                {
                    [FeedbackAction] public static void Play(string message) { }
                }
                """);
            Assert.NotNull(assembly.GetType("First+Actions+Play"));
            Assert.NotNull(assembly.GetType("Second+Actions+Play"));
            Assert.Null(assembly.GetType("First+Actions+Play_int"));
            Assert.Null(assembly.GetType("Second+Actions+Play_string"));
        }

        [Fact]
        public void OverloadsAcrossPartialDeclarationsUseParameterTypes()
        {
            var assembly = Compile("""
                using HKFeedbackV2;
                public partial class Effects
                {
                    [FeedbackAction] public static void Play(int count) { }
                }
                public partial class Effects
                {
                    [FeedbackAction] public static void Play(string message) { }
                    [FeedbackAction] public static void Stop(int count) { }
                }
                """);
            Assert.Null(assembly.GetType("Effects+Actions+Play"));
            Assert.NotNull(assembly.GetType("Effects+Actions+Play_int"));
            Assert.NotNull(assembly.GetType("Effects+Actions+Play_string"));
            Assert.NotNull(assembly.GetType("Effects+Actions+Stop"));
        }

        [Theory]
        [InlineData("", "")]
        [InlineData("namespace Game;", "Game")]
        [InlineData("namespace @event.Game;", "event.Game")]
        public void ShortenedClassNamesKeepPreviousSerializedTypeNames(string namespaceDeclaration, string namespaceName)
        {
            var assembly = Compile($$"""
                using HKFeedbackV2;
                {{namespaceDeclaration}}
                public partial class Outer
                {
                    public partial class Effects
                    {
                        [FeedbackAction] public static void Play(int count) { }
                        [FeedbackAction] public static void Stop() { }
                    }
                }
                """);
            var prefix = namespaceName.Length == 0 ? "" : namespaceName + ".";
            var action = assembly.GetType(prefix + "Outer+Effects+Actions+Play")!;
            var migration = action.GetCustomAttribute<UnityEngine.Scripting.APIUpdating.MovedFromAttribute>()!;
            Assert.Equal("Outer/Effects/Actions/Play_int", migration.SourceClassName);
            Assert.Equal(namespaceName, migration.SourceNamespace);
            Assert.Equal(assembly.GetName().Name, migration.SourceAssembly);
            Assert.Null(assembly.GetType(prefix + "Outer+Effects+Actions+Stop")!
                .GetCustomAttribute<UnityEngine.Scripting.APIUpdating.MovedFromAttribute>());
        }

        [Fact]
        public void SerializedTypeMigrationDoesNotOverrideAnExistingActionName()
        {
            var assembly = Compile("""
                using HKFeedbackV2;
                public partial class Effects
                {
                    [FeedbackAction] public static void Play(int count) { }
                    [FeedbackAction] public static void Play_int() { }
                }
                """);
            Assert.NotNull(assembly.GetType("Effects+Actions+Play_int"));
            Assert.Null(assembly.GetType("Effects+Actions+Play")!
                .GetCustomAttribute<UnityEngine.Scripting.APIUpdating.MovedFromAttribute>());
        }

        [Fact]
        public async Task UniTaskOverloadsNamedLikeRequiredOverrideRemainCallable()
        {
            var assembly = Compile("""
                using HKFeedbackV2;
                using Cysharp.Threading.Tasks;
                public static partial class Effects
                {
                    public static int Value;
                    [FeedbackAction] private static UniTask InvokeAsyncInternal(int value = 7)
                    {
                        Value = value;
                        return UniTask.CompletedTask;
                    }
                    [FeedbackAction] private static UniTask InvokeAsyncInternal(string value = "hello")
                    {
                        Value = value.Length;
                        return UniTask.CompletedTask;
                    }
                }
                """);
            await CreateAction(assembly, "Effects+Actions+InvokeAsyncInternal_int").InvokeAsync(new Container(), default);
            Assert.Equal(7, assembly.GetType("Effects")!.GetField("Value")!.GetValue(null));
            await CreateAction(assembly, "Effects+Actions+InvokeAsyncInternal_string").InvokeAsync(new Container(), default);
            Assert.Equal(5, assembly.GetType("Effects")!.GetField("Value")!.GetValue(null));
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
            SetResolver(action, "value", new SerializeField<object>(42));
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
            await CreateAction(assembly, "Effects+Actions+" + methodName).InvokeAsync(container, default);
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

        [Fact]
        public async Task ParameterNamesCanBeKeywordsAndMatchExecutionContextNames()
        {
            var assembly = Compile("""
                using HKFeedbackV2;
                public static partial class Effects
                {
                    public static int Value;
                    [FeedbackAction] private static void Play(int @event = 2, int container = 3, int cancellationToken = 4)
                    {
                        Value = @event * 100 + container * 10 + cancellationToken;
                    }
                }
                """);
            var action = CreateAction(assembly, "Effects+Actions+Play");
            SetResolver(action, "event", new SerializeField<int>(5));
            await action.InvokeAsync(new Container(), default);
            Assert.Equal(534, assembly.GetType("Effects")!.GetField("Value")!.GetValue(null));
            Assert.NotNull(action.GetType().GetField("container", BindingFlags.NonPublic | BindingFlags.Instance));
            Assert.NotNull(action.GetType().GetField("cancellationToken", BindingFlags.NonPublic | BindingFlags.Instance));
        }

        [Theory]
        [InlineData("_target", "__target")]
        [InlineData("InvokeAsync", "_InvokeAsync")]
        [InlineData("InvokeAsyncInternal", "_InvokeAsyncInternal")]
        [InlineData("Play", "_Play")]
        [InlineData("ToString", "_ToString")]
        public async Task ConflictingParameterNamesKeepLabelsAndRemainCallable(string parameterName, string fieldName)
        {
            var assembly = Compile($$"""
                using HKFeedbackV2;
                public partial class Effects
                {
                    public int Value;
                    [FeedbackAction] private void Play(int @{{parameterName}} = 7) { Value = @{{parameterName}}; }
                }
                """);
            var owner = assembly.GetType("Effects")!;
            var target = Activator.CreateInstance(owner)!;
            var container = new Container();
            container.Register(ContainerKey.Anonymous, target);
            var action = CreateAction(assembly, "Effects+Actions+Play");
            SetResolver(action, fieldName, new SerializeField<int>(19));
            await action.InvokeAsync(container, default);
            Assert.Equal(19, owner.GetField("Value")!.GetValue(target));
            var field = action.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)!;
            Assert.Equal(parameterName, field.GetCustomAttribute<UnityEngine.InspectorName>()!.displayName);
        }

        [Fact]
        public async Task ReceiverAndParameterFieldNamesDoNotCollide()
        {
            var assembly = Compile("""
                using HKFeedbackV2;
                public partial class Effects
                {
                    public int Value;
                    [FeedbackAction] private void Play(int _target = 1, int __target = 2, int _argument3 = 3, int _argument2 = 4)
                    {
                        Value = _target + __target + _argument3 + _argument2;
                    }
                }
                """);
            var owner = assembly.GetType("Effects")!;
            var target = Activator.CreateInstance(owner)!;
            var container = new Container();
            container.Register(ContainerKey.Anonymous, target);
            var action = CreateAction(assembly, "Effects+Actions+Play");
            await action.InvokeAsync(container, default);
            Assert.Equal(10, owner.GetField("Value")!.GetValue(target));
            var fields = action.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.Equal(5, fields.Length);
            Assert.Equal(fields.Length, fields.Select(field => field.Name).Distinct().Count());
            Assert.NotNull(action.GetType().GetField("__target", BindingFlags.NonPublic | BindingFlags.Instance));
        }

        [Fact]
        public async Task ParameterNamedIsWaitDoesNotReplaceInheritedWaitSetting()
        {
            var assembly = Compile("""
                using HKFeedbackV2;
                using Cysharp.Threading.Tasks;
                public static partial class Effects
                {
                    public static bool Value;
                    [FeedbackAction] private static UniTask Play(bool isWait = true)
                    {
                        Value = isWait;
                        return UniTask.CompletedTask;
                    }
                }
                """);
            var action = CreateAction(assembly, "Effects+Actions+Play");
            SetResolver(action, "_isWait", new SerializeField<bool>(false));
            var container = new Container();
            await action.InvokeAsync(container, default);
            Assert.Equal(false, assembly.GetType("Effects")!.GetField("Value")!.GetValue(null));
            var waitSetting = (IResolver<bool>)typeof(HKFeedbackV2.Actions.AsyncAction)
                .GetField("isWait", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(action)!;
            Assert.True(waitSetting.Resolve(container));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void GeneratedSubclassSelectorsFollowTheDefine(bool enabled)
        {
            const string source = """
                using HKFeedbackV2;
                public partial class Effects
                {
                    [FeedbackAction] private void Play(int value) { }
                    [FeedbackAction] private static void Log(string message) { }
                }
                """;
            var generation = Run(source, enabled);
            AssertSuccess(generation);
            if (!enabled && typeof(IAction).Assembly.GetType("SubclassSelectorAttribute") == null)
            {
                Assert.Null(generation.Compilation.GetTypeByMetadataName("SubclassSelectorAttribute"));
            }
            var assembly = Emit(generation);
            var fields = assembly.GetType("Effects+Actions+Play")!
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Concat(assembly.GetType("Effects+Actions+Log")!
                    .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)).ToArray();
            Assert.Equal(3, fields.Length);
            foreach (var field in fields)
            {
                Assert.True(field.IsDefined(typeof(UnityEngine.SerializeReference)));
                Assert.Equal(enabled, HasSubclassSelector(field));
            }
        }

        [Fact]
        public void RuntimeSubclassSelectorsFollowTheBuildDefine()
        {
#if HK_FEEDBACK_V2_SERIALIZE_REFERENCE_EXTENSIONS
            const bool enabled = true;
#else
            const bool enabled = false;
#endif
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var fields = new[]
            {
                typeof(HKFeedbackV2.Actions.AsyncAction).GetField("isWait", flags)!,
                typeof(Anonymous<int>).GetField("canBeNull", flags)!,
                typeof(HKFeedbackV2.Resolvers.ContainerKey<int>).GetField("canBeNull", flags)!,
                typeof(HKFeedbackV2.Resolvers.ContainerKey<int>).GetField("key", flags)!,
                typeof(SerializeReference<IEvaluator>).GetField("value", flags)!
            };
            foreach (var field in fields)
            {
                Assert.True(field.IsDefined(typeof(UnityEngine.SerializeReference)));
                Assert.Equal(enabled, HasSubclassSelector(field));
            }
        }

        private static bool HasSubclassSelector(FieldInfo field) =>
            field.GetCustomAttributes().Any(attribute => attribute.GetType().FullName == "SubclassSelectorAttribute");

        private static Generation Run(string source, bool enableSerializeReferenceExtensions = false)
        {
            var parseOptions = enableSerializeReferenceExtensions
                ? ParseOptions.WithPreprocessorSymbols("HK_FEEDBACK_V2_SERIALIZE_REFERENCE_EXTENSIONS")
                : ParseOptions;
            var syntaxTrees = new List<SyntaxTree> { CSharpSyntaxTree.ParseText(source, parseOptions) };
            if (enableSerializeReferenceExtensions && typeof(IAction).Assembly.GetType("SubclassSelectorAttribute") == null)
            {
                syntaxTrees.Add(CSharpSyntaxTree.ParseText("""
                    [System.AttributeUsage(System.AttributeTargets.Field)]
                    public sealed class SubclassSelectorAttribute : System.Attribute { }
                    """, parseOptions));
            }
            var compilation = CSharpCompilation.Create("GeneratedActions_" + Guid.NewGuid().ToString("N"),
                syntaxTrees, References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new[] { new FeedbackActionGenerator().AsSourceGenerator() }, parseOptions: parseOptions);
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
            return Emit(generation);
        }

        private static Assembly Emit(Generation generation)
        {
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
