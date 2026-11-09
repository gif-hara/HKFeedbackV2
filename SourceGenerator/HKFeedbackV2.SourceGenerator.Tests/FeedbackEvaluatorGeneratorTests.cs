#nullable enable
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HKFeedbackV2.Resolvers;
using Xunit;
using static HKFeedbackV2.SourceGenerator.Tests.FeedbackGeneratorTestDriver;

namespace HKFeedbackV2.SourceGenerator.Tests
{
    public sealed class FeedbackEvaluatorGeneratorTests
    {
        [Fact]
        public void StaticEvaluatorResolvesNamedArgumentsAndInjectsContainerOnEachCall()
        {
            var assembly = Compile("""
                using HKFeedbackV2;
                namespace Game;
                public static partial class Conditions
                {
                    public static IContainer Context;
                    public static int Calls;
                    public static IEvaluator Create() => new Evaluators.Check();
                    [FeedbackEvaluator]
                    private static bool Check(int value = 2, int minimum = 1, IContainer context = null)
                    {
                        Context = context;
                        Calls++;
                        return value > minimum;
                    }
                }
                """);
            var evaluator = CreateEvaluator(assembly, "Game.Conditions+Evaluators+Check");
            Assert.True(evaluator.Evaluate(new Container()));
            SetField(evaluator, "value", new SerializeField<int>(0));
            var container = new Container();
            Assert.False(evaluator.Evaluate(container));
            var owner = assembly.GetType("Game.Conditions")!;
            Assert.Same(container, owner.GetField("Context")!.GetValue(null));
            Assert.Equal(2, owner.GetField("Calls")!.GetValue(null));
            Assert.True(evaluator.GetType().IsDefined(typeof(SerializableAttribute)));
            Assert.Equal(new[] { "minimum", "value" }, evaluator.GetType()
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance).Select(field => field.Name).OrderBy(name => name));
        }

        [Fact]
        public void PrivateInstanceEvaluatorUsesAnonymousTargetAndAllowsReplacingIt()
        {
            var assembly = Compile("""
                using HKFeedbackV2;
                public partial class Conditions
                {
                    public int Value;
                    [FeedbackEvaluator] private bool Check(int minimum = 7) => Value > minimum;
                }
                """);
            var owner = assembly.GetType("Conditions")!;
            var target = Activator.CreateInstance(owner)!;
            owner.GetField("Value")!.SetValue(target, 10);
            var container = new Container();
            container.Register(ContainerKey.Anonymous, target);
            var evaluator = CreateEvaluator(assembly, "Conditions+Evaluators+Check");
            Assert.True(evaluator.Evaluate(container));
            var replacement = Activator.CreateInstance(owner)!;
            owner.GetField("Value")!.SetValue(replacement, 3);
            SetField(evaluator, "_Conditions", Activator.CreateInstance(typeof(SerializeField<>).MakeGenericType(owner), replacement)!);
            Assert.False(evaluator.Evaluate(container));
        }

        [Fact]
        public void OverloadsAcrossPartialDeclarationsUseParameterTypesOnlyForRepeatedNames()
        {
            var assembly = Compile("""
                using HKFeedbackV2;
                public static partial class Conditions
                {
                    [FeedbackEvaluator] private static bool Check() => true;
                    [FeedbackEvaluator] private static bool Check(int value = 1) => value > 0;
                }
                public static partial class Conditions
                {
                    [FeedbackEvaluator] private static bool Check(string value = "hello") => value.Length > 0;
                    [FeedbackEvaluator] private static bool Check(IContainer context) => context != null;
                    [FeedbackEvaluator] private static bool Unique(int value = 1) => value > 0;
                    private static bool Unique(string value) => value.Length > 0;
                }
                public static partial class Other
                {
                    [FeedbackEvaluator] private static bool Check(int value = 2) => value == 2;
                }
                """);
            var container = new Container();
            foreach (var name in new[] { "Check", "Check_int", "Check_string", "Check_IContainer", "Unique" })
                Assert.True(CreateEvaluator(assembly, "Conditions+Evaluators+" + name).Evaluate(container));
            Assert.True(CreateEvaluator(assembly, "Other+Evaluators+Check").Evaluate(container));
            Assert.Null(assembly.GetType("Conditions+Evaluators+Unique_int"));
            Assert.Null(assembly.GetType("Other+Evaluators+Check_int"));
        }

        [Theory]
        [InlineData("Evaluate")]
        [InlineData("_target")]
        [InlineData("_argument0")]
        [InlineData("switch")]
        public void MethodNamesCanMatchGeneratedMembersAndKeywords(string methodName)
        {
            var assembly = Compile($$"""
                using HKFeedbackV2;
                public partial class Conditions
                {
                    [FeedbackEvaluator] private bool @{{methodName}}(int value = 1) => value > 0;
                }
                """);
            var container = new Container();
            container.Register(ContainerKey.Anonymous, Activator.CreateInstance(assembly.GetType("Conditions")!)!);
            Assert.True(CreateEvaluator(assembly, "Conditions+Evaluators+" + methodName).Evaluate(container));
        }

        [Theory]
        [InlineData("_target", "_target")]
        [InlineData("Evaluate", "_Evaluate")]
        [InlineData("Check", "_Check")]
        [InlineData("ToString", "_ToString")]
        [InlineData("container", "container")]
        [InlineData("event", "event")]
        public void ParameterFieldsUseOriginalLabelsAndRemainCallable(string parameterName, string fieldName)
        {
            var assembly = Compile($$"""
                using HKFeedbackV2;
                public partial class Conditions
                {
                    [FeedbackEvaluator] private bool Check(int @{{parameterName}} = 0) => @{{parameterName}} == 19;
                }
                """);
            var container = new Container();
            container.Register(ContainerKey.Anonymous, Activator.CreateInstance(assembly.GetType("Conditions")!)!);
            var evaluator = CreateEvaluator(assembly, "Conditions+Evaluators+Check");
            Assert.False(evaluator.Evaluate(container));
            SetField(evaluator, fieldName, new SerializeField<int>(19));
            Assert.True(evaluator.Evaluate(container));
            Assert.Equal(parameterName, evaluator.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetCustomAttribute<UnityEngine.InspectorName>()!.displayName);
        }

        [Fact]
        public void CancellationTokenIsResolvedAsAnOrdinaryArgument()
        {
            var assembly = Compile("""
                using HKFeedbackV2;
                using System.Threading;
                public static partial class Conditions
                {
                    [FeedbackEvaluator] private static bool IsCancelled(CancellationToken token = default) => token.IsCancellationRequested;
                }
                """);
            var evaluator = CreateEvaluator(assembly, "Conditions+Evaluators+IsCancelled");
            var container = new Container();
            Assert.False(evaluator.Evaluate(container));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            SetField(evaluator, "token", new SerializeField<CancellationToken>(cancellation.Token));
            Assert.True(evaluator.Evaluate(container));
        }

        [Fact]
        public async Task EvaluatorsCanUseGeneratedResolversAndShareAnOwnerWithOtherGenerators()
        {
            var assembly = Compile("""
                using HKFeedbackV2;
                public static partial class Conditions
                {
                    public static int Result;
                    [FeedbackResolver] private static int Current(int value = 3) => value;
                    [FeedbackEvaluator, FeedbackResolver] private static bool Positive(int value) => value > 0;
                    [FeedbackAction] private static void Positive(string text = "ready") { Result = text.Length; }
                    public static IEvaluator Create() => new Evaluators.Positive();
                }
                """);
            var container = new Container();
            var evaluator = CreateEvaluator(assembly, "Conditions+Evaluators+Positive");
            var input = (IResolver<int>)Activator.CreateInstance(assembly.GetType("Conditions+Resolvers+Current")!)!;
            SetField(evaluator, "value", input);
            Assert.True(evaluator.Evaluate(container));
            SetField(input, "value", new SerializeField<int>(-3));
            Assert.False(evaluator.Evaluate(container));
            var booleanResolver = (IResolver<bool>)Activator.CreateInstance(assembly.GetType("Conditions+Resolvers+Positive")!)!;
            SetField(booleanResolver, "value", input);
            Assert.False(booleanResolver.Resolve(container));
            var action = (IAction)Activator.CreateInstance(assembly.GetType("Conditions+Actions+Positive")!)!;
            await action.InvokeAsync(container, default);
            Assert.Equal(5, assembly.GetType("Conditions")!.GetField("Result")!.GetValue(null));
        }

        [Fact]
        public void PrivateNestedRecordOwnersAliasesAndImplementedPartialMethodsAreSupported()
        {
            var assembly = Compile("""
                using HKFeedbackV2;
                using Mark = HKFeedbackV2.FeedbackEvaluatorAttribute;
                namespace @event;
                public partial record class Outer
                {
                    public static IEvaluator Create() => new Inner.Evaluators.@switch();
                    private partial class Inner
                    {
                        [Mark] private static partial bool @switch(int @event = 1);
                    }
                    private partial class Inner
                    {
                        private static partial bool @switch(int @event) => @event > 0;
                    }
                }
                """);
            var evaluator = (IEvaluator)assembly.GetType("event.Outer")!.GetMethod("Create")!.Invoke(null, null)!;
            Assert.True(evaluator.Evaluate(new Container()));
            Assert.Equal("switch", evaluator.GetType().Name);
        }

        [Theory]
        [InlineData("void", "{ }")]
        [InlineData("int", "=> 1;")]
        [InlineData("bool?", "=> true;")]
        [InlineData("object", "=> true;")]
        [InlineData("Cysharp.Threading.Tasks.UniTask", "=> Cysharp.Threading.Tasks.UniTask.CompletedTask;")]
        [InlineData("System.Threading.Tasks.Task<bool>", "=> System.Threading.Tasks.Task.FromResult(true);")]
        public void OnlyBooleanReturnValuesAreAccepted(string returnType, string body)
        {
            var generation = Run($$"""
                using HKFeedbackV2;
                public static partial class Conditions
                {
                    [FeedbackEvaluator] private static {{returnType}} Check() {{body}}
                }
                """);
            Assert.Equal("HKFSG014", Assert.Single(generation.Result.Diagnostics).Id);
            Assert.Empty(generation.Result.Results.SelectMany(result => result.GeneratedSources));
        }

        [Theory]
        [InlineData("[FeedbackEvaluator] public static bool Check<T>() => true;", "HKFSG013")]
        [InlineData("[FeedbackEvaluator] public abstract bool Check();", "HKFSG013")]
        [InlineData("private static bool value; [FeedbackEvaluator] public static ref bool Check() => ref value;", "HKFSG013")]
        [InlineData("[FeedbackEvaluator] public static bool Check(ref int value) => value > 0;", "HKFSG016")]
        [InlineData("[FeedbackEvaluator] public static bool Check(in int value) => value > 0;", "HKFSG016")]
        [InlineData("[FeedbackEvaluator] public static bool Check(out int value) { value = 0; return true; }", "HKFSG016")]
        [InlineData("[FeedbackEvaluator] public static bool Check(System.Span<int> value) => true;", "HKFSG016")]
        [InlineData("[FeedbackEvaluator] public static unsafe bool Check(int* value) => true;", "HKFSG016")]
        public void UnsupportedDeclarationsReportActionableDiagnostics(string declaration, string diagnosticId)
        {
            var generation = Run("using HKFeedbackV2; public abstract partial class Conditions { " + declaration + " }");
            var diagnostic = Assert.Single(generation.Result.Diagnostics);
            Assert.Equal(diagnosticId, diagnostic.Id);
            Assert.True(diagnostic.Location.IsInSource);
            Assert.Empty(generation.Result.Results.SelectMany(result => result.GeneratedSources));
        }

        [Theory]
        [InlineData("public class Conditions { [FeedbackEvaluator] public static bool Check() => true; }", "HKFSG017")]
        [InlineData("public class Outer { public partial class Conditions { [FeedbackEvaluator] public static bool Check() => true; } }", "HKFSG017")]
        [InlineData("public partial class Conditions<T> { [FeedbackEvaluator] public static bool Check() => true; }", "HKFSG013")]
        [InlineData("public struct Conditions { [FeedbackEvaluator] public static bool Check() => true; }", "HKFSG013")]
        public void UnsupportedContainingTypesAreDiagnosed(string declaration, string diagnosticId)
        {
            Assert.Equal(diagnosticId, Assert.Single(Run("using HKFeedbackV2; " + declaration).Result.Diagnostics).Id);
        }

        [Theory]
        [InlineData("public partial class Evaluators { [FeedbackEvaluator] public static bool Check() => true; }")]
        [InlineData("public partial class Conditions { public static class Evaluators { } [FeedbackEvaluator] public static bool Check() => true; }")]
        [InlineData("public partial class Conditions { public int Evaluators; [FeedbackEvaluator] public static bool Check() => true; }")]
        [InlineData("public partial class Conditions { [FeedbackEvaluator] public static bool Evaluators() => true; }")]
        public void NamesConflictingWithTheEvaluatorsContainerAreDiagnosed(string declaration)
        {
            Assert.Equal("HKFSG015", Assert.Single(Run("using HKFeedbackV2; " + declaration).Result.Diagnostics).Id);
        }

        [Fact]
        public void NormalizedNameCollisionsAreDiagnosedWithoutSuppressingOtherEvaluators()
        {
            var generation = Run("""
                using HKFeedbackV2;
                public partial class Conditions
                {
                    [FeedbackEvaluator] public static bool Check(int value) => value > 0;
                    [FeedbackEvaluator] public static bool Check(string value) => value.Length > 0;
                    [FeedbackEvaluator] public static bool Check_int() => true;
                }
                """);
            Assert.Equal(2, generation.Result.Diagnostics.Length);
            Assert.All(generation.Result.Diagnostics, diagnostic => Assert.Equal("HKFSG015", diagnostic.Id));
            var assembly = Emit(generation);
            Assert.NotNull(assembly.GetType("Conditions+Evaluators+Check_string"));
            Assert.Null(assembly.GetType("Conditions+Evaluators+Check_int"));
        }

        [Fact]
        public void UnrelatedAttributesAndUnannotatedMethodsAreIgnored()
        {
            var generation = Run("""
                using System;
                namespace Other;
                public class FeedbackEvaluatorAttribute : Attribute { }
                public partial class Conditions
                {
                    [FeedbackEvaluator] public static bool Check() => true;
                    public static bool Unannotated() => true;
                }
                """);
            AssertSuccess(generation);
            Assert.Empty(generation.Result.Results.SelectMany(result => result.GeneratedSources));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void GeneratedSubclassSelectorsFollowTheDefine(bool enabled)
        {
            var generation = Run("""
                using HKFeedbackV2;
                public partial class Conditions
                {
                    [FeedbackEvaluator] private bool Check(int value) => value > 0;
                }
                """, enabled);
            AssertSuccess(generation);
            if (!enabled && typeof(IEvaluator).Assembly.GetType("SubclassSelectorAttribute") == null)
                Assert.Null(generation.Compilation.GetTypeByMetadataName("SubclassSelectorAttribute"));
            var fields = Emit(generation).GetType("Conditions+Evaluators+Check")!
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.Equal(2, fields.Length);
            foreach (var field in fields)
            {
                Assert.True(field.IsDefined(typeof(UnityEngine.SerializeReference)));
                Assert.Equal(enabled, field.GetCustomAttributes().Any(attribute => attribute.GetType().Name == "SubclassSelectorAttribute"));
            }
        }

        private static IEvaluator CreateEvaluator(Assembly assembly, string name) =>
            (IEvaluator)Activator.CreateInstance(assembly.GetType(name, throwOnError: true)!)!;

        private static void SetField(object instance, string fieldName, object value) => instance.GetType()
            .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(instance, value);
    }
}
