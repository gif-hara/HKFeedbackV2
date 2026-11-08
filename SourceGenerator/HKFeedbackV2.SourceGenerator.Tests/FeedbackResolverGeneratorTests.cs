#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HKFeedbackV2.Resolvers;
using Xunit;
using static HKFeedbackV2.SourceGenerator.Tests.FeedbackGeneratorTestDriver;

namespace HKFeedbackV2.SourceGenerator.Tests
{
    public sealed class FeedbackResolverGeneratorTests
    {
        [Fact]
        public void StaticResolverResolvesNamedArgumentsAndInjectsContainerOnEachCall()
        {
            var assembly = Compile("""
                using HKFeedbackV2;
                namespace Game;
                public static partial class Calculator
                {
                    public static IContainer Context;
                    public static int Calls;
                    public static IResolver<int> Create() => new Resolvers.Calculate();
                    [FeedbackResolver]
                    private static int Calculate(int value = 3, string label = "hi", IContainer context = null)
                    {
                        Context = context;
                        Calls++;
                        return value + label.Length;
                    }
                }
                """);
            var resolver = CreateResolver<int>(assembly, "Game.Calculator+Resolvers+Calculate");
            var container = new Container();
            Assert.Equal(5, resolver.Resolve(container));
            SetField(resolver, "value", new SerializeField<int>(42));
            Assert.Equal(44, resolver.Resolve(container));
            var owner = assembly.GetType("Game.Calculator")!;
            Assert.Same(container, owner.GetField("Context")!.GetValue(null));
            Assert.Equal(2, owner.GetField("Calls")!.GetValue(null));
            Assert.True(resolver.GetType().IsDefined(typeof(SerializableAttribute)));
            Assert.Equal(new[] { "label", "value" }, resolver.GetType()
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Select(field => field.Name).OrderBy(name => name));
        }

        [Fact]
        public void PrivateInstanceResolverUsesAnonymousTargetAndAllowsReplacingIt()
        {
            var assembly = Compile("""
                using HKFeedbackV2;
                public partial class Stats
                {
                    public int Value;
                    [FeedbackResolver] private int Add(int amount = 7) => Value + amount;
                }
                """);
            var owner = assembly.GetType("Stats")!;
            var target = Activator.CreateInstance(owner)!;
            owner.GetField("Value")!.SetValue(target, 10);
            var container = new Container();
            container.Register(ContainerKey.Anonymous, target);
            var resolver = CreateResolver<int>(assembly, "Stats+Resolvers+Add");
            Assert.Equal(17, resolver.Resolve(container));
            var replacement = Activator.CreateInstance(owner)!;
            owner.GetField("Value")!.SetValue(replacement, 20);
            SetField(resolver, "_target", Activator.CreateInstance(typeof(SerializeField<>).MakeGenericType(owner), replacement)!);
            Assert.Equal(27, resolver.Resolve(container));
        }

        [Fact]
        public void CancellationTokenIsResolvedAsAnOrdinaryArgument()
        {
            var assembly = Compile("""
                using HKFeedbackV2;
                using System.Threading;
                public static partial class Values
                {
                    [FeedbackResolver] public static bool IsCancelled(CancellationToken token) => token.IsCancellationRequested;
                }
                """);
            var resolver = CreateResolver<bool>(assembly, "Values+Resolvers+IsCancelled");
            using var cancellation = new CancellationTokenSource();
            SetField(resolver, "token", new SerializeField<CancellationToken>(cancellation.Token));
            Assert.False(resolver.Resolve(new Container()));
            cancellation.Cancel();
            Assert.True(resolver.Resolve(new Container()));
        }

        [Fact]
        public void OverloadsAcrossPartialDeclarationsUseParameterTypesOnlyForRepeatedNames()
        {
            var assembly = Compile("""
                using HKFeedbackV2;
                public static partial class Values
                {
                    [FeedbackResolver] private static int Read() => 1;
                    [FeedbackResolver] private static int Read(int value = 7) => value;
                }
                public static partial class Values
                {
                    [FeedbackResolver] private static string Read(string value = "hello") => value;
                    [FeedbackResolver] private static int Read(IContainer context) => context == null ? 0 : 10;
                    [FeedbackResolver] private static int Unique(int value = 3) => value;
                    private static string Unique(string value) => value;
                }
                """);
            var container = new Container();
            Assert.Equal(1, CreateResolver<int>(assembly, "Values+Resolvers+Read").Resolve(container));
            Assert.Equal(7, CreateResolver<int>(assembly, "Values+Resolvers+Read_int").Resolve(container));
            Assert.Equal("hello", CreateResolver<string>(assembly, "Values+Resolvers+Read_string").Resolve(container));
            Assert.Equal(10, CreateResolver<int>(assembly, "Values+Resolvers+Read_IContainer").Resolve(container));
            Assert.Equal(3, CreateResolver<int>(assembly, "Values+Resolvers+Unique").Resolve(container));
            Assert.Null(assembly.GetType("Values+Resolvers+Unique_int"));
        }

        [Fact]
        public void PrivateNestedClassesAliasesKeywordsAndRecordClassesAreSupported()
        {
            var assembly = Compile("""
                using HKFeedbackV2;
                using Mark = HKFeedbackV2.FeedbackResolverAttribute;
                namespace @event;
                public partial record class Outer
                {
                    public static IResolver<int> Create() => new Inner.Resolvers.@switch();
                    private partial class Inner
                    {
                        [Mark] private static int @switch(int @event = 5) => @event;
                    }
                }
                """);
            var resolver = (IResolver<int>)assembly.GetType("event.Outer")!.GetMethod("Create")!.Invoke(null, null)!;
            Assert.Equal(5, resolver.Resolve(new Container()));
            Assert.NotNull(resolver.GetType().GetField("event", BindingFlags.NonPublic | BindingFlags.Instance));
        }

        [Theory]
        [InlineData("Resolve")]
        [InlineData("_target")]
        [InlineData("_argument0")]
        public void MethodNamesCanMatchGeneratedMembers(string methodName)
        {
            var assembly = Compile($$"""
                using HKFeedbackV2;
                public partial class Values
                {
                    [FeedbackResolver] private int {{methodName}}(int value = 7) => value;
                }
                """);
            var owner = assembly.GetType("Values")!;
            var container = new Container();
            container.Register(ContainerKey.Anonymous, Activator.CreateInstance(owner)!);
            Assert.Equal(7, CreateResolver<int>(assembly, "Values+Resolvers+" + methodName).Resolve(container));
        }

        [Theory]
        [InlineData("_target", "__target")]
        [InlineData("Resolve", "_Resolve")]
        [InlineData("Read", "_Read")]
        [InlineData("ToString", "_ToString")]
        [InlineData("container", "container")]
        [InlineData("event", "event")]
        public void ConflictingAndKeywordParameterNamesRemainCallableWithOriginalLabels(string parameterName, string fieldName)
        {
            var assembly = Compile($$"""
                using HKFeedbackV2;
                public partial class Values
                {
                    [FeedbackResolver] private int Read(int @{{parameterName}} = 7) => @{{parameterName}};
                }
                """);
            var container = new Container();
            container.Register(ContainerKey.Anonymous, Activator.CreateInstance(assembly.GetType("Values")!)!);
            var resolver = CreateResolver<int>(assembly, "Values+Resolvers+Read");
            SetField(resolver, fieldName, new SerializeField<int>(19));
            Assert.Equal(19, resolver.Resolve(container));
            Assert.Equal(parameterName, resolver.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetCustomAttribute<UnityEngine.InspectorName>()!.displayName);
        }

        [Fact]
        public void DynamicArgumentsBindToTheirDeclaredOverload()
        {
            var assembly = Compile("""
                using HKFeedbackV2;
                public static partial class Values
                {
                    [FeedbackResolver] private static string Read(dynamic value) => "object";
                    [FeedbackResolver] private static string Read(int value) => "int";
                }
                """);
            var resolver = CreateResolver<string>(assembly, "Values+Resolvers+Read_object");
            SetField(resolver, "value", new SerializeField<object>(42));
            Assert.Equal("object", resolver.Resolve(new Container()));
        }

        [Fact]
        public void GenericAndTupleReturnValuesAndOptionalConstantsArePreserved()
        {
            var assembly = Compile("""
                using HKFeedbackV2;
                using System;
                using System.Collections.Generic;
                public static partial class Values
                {
                    [FeedbackResolver] private static List<(string, int)> Read(
                        string text = "line\n\"quoted\"", int count = 7, char quote = '\'',
                        decimal amount = 1.5m, float nan = float.NaN, double infinity = double.PositiveInfinity,
                        DayOfWeek day = DayOfWeek.Friday, int? optional = null, int[] items = null)
                        => new List<(string, int)> { (text + quote, count) };
                }
                """);
            var resolver = CreateResolver<List<(string, int)>>(assembly, "Values+Resolvers+Read");
            Assert.Equal(("line\n\"quoted\"'", 7), Assert.Single(resolver.Resolve(new Container())));
        }

        [Fact]
        public async Task TaskReturnValuesAreReturnedWithoutUnwrapping()
        {
            var assembly = Compile("""
                using HKFeedbackV2;
                using System.Threading.Tasks;
                public static partial class Values
                {
                    [FeedbackResolver] private static Task<int> Read() => Task.FromResult(42);
                }
                """);
            Assert.Equal(42, await CreateResolver<Task<int>>(assembly, "Values+Resolvers+Read").Resolve(new Container()));
        }

        [Fact]
        public async Task GeneratedResolversCanSupplyArgumentsToGeneratedActions()
        {
            var assembly = Compile("""
                using HKFeedbackV2;
                public static partial class Values
                {
                    public static int Result;
                    [FeedbackResolver] private static int Double(int value = 3) => value * 2;
                    [FeedbackAction] private static void Store(int value) { Result = value; }
                    public static IAction Create()
                    {
                        var action = new Actions.Store();
                        return action;
                    }
                }
                """);
            var resolver = CreateResolver<int>(assembly, "Values+Resolvers+Double");
            var action = (IAction)Activator.CreateInstance(assembly.GetType("Values+Actions+Store")!)!;
            SetField(action, "value", resolver);
            await action.InvokeAsync(new Container(), default);
            Assert.Equal(6, assembly.GetType("Values")!.GetField("Result")!.GetValue(null));
        }

        [Fact]
        public async Task ActionsAndResolversCanShareAnOwnerAndAnnotatedMethod()
        {
            var assembly = Compile("""
                using HKFeedbackV2;
                using Cysharp.Threading.Tasks;
                public static partial class Values
                {
                    public static int Calls;
                    [FeedbackAction, FeedbackResolver] private static UniTask Shared()
                    {
                        Calls++;
                        return UniTask.CompletedTask;
                    }
                    [FeedbackAction] private static void Read(int value) { }
                    [FeedbackResolver] private static int Read(string text = "hello") => text.Length;
                }
                """);
            var container = new Container();
            await CreateResolver<Cysharp.Threading.Tasks.UniTask>(assembly, "Values+Resolvers+Shared").Resolve(container);
            await ((IAction)Activator.CreateInstance(assembly.GetType("Values+Actions+Shared")!)!).InvokeAsync(container, default);
            Assert.Equal(2, assembly.GetType("Values")!.GetField("Calls")!.GetValue(null));
            Assert.Equal(5, CreateResolver<int>(assembly, "Values+Resolvers+Read").Resolve(container));
            Assert.NotNull(assembly.GetType("Values+Actions+Read"));
        }

        [Fact]
        public void ImplementedPartialMethodGeneratesOneUnsuffixedResolver()
        {
            var assembly = Compile("""
                using HKFeedbackV2;
                public static partial class Values
                {
                    [FeedbackResolver] public static partial int Read(int value = 3);
                }
                public static partial class Values
                {
                    public static partial int Read(int value) => value;
                }
                """);
            Assert.Equal(3, CreateResolver<int>(assembly, "Values+Resolvers+Read").Resolve(new Container()));
            Assert.Null(assembly.GetType("Values+Resolvers+Read_int"));
        }

        [Theory]
        [InlineData("[FeedbackResolver] public static void Read() { }", "HKFSG009")]
        [InlineData("[FeedbackResolver] public static int Read<T>() => 0;", "HKFSG008")]
        [InlineData("[FeedbackResolver] public abstract int Read();", "HKFSG008")]
        [InlineData("private static int value; [FeedbackResolver] public static ref int Read() => ref value;", "HKFSG008")]
        [InlineData("[FeedbackResolver] public static System.Span<int> Read() => default;", "HKFSG009")]
        [InlineData("[FeedbackResolver] public static unsafe int* Read() => null;", "HKFSG009")]
        [InlineData("[FeedbackResolver] public static int Read(ref int value) => value;", "HKFSG011")]
        [InlineData("[FeedbackResolver] public static int Read(in int value) => value;", "HKFSG011")]
        [InlineData("[FeedbackResolver] public static int Read(out int value) { value = 0; return 0; }", "HKFSG011")]
        [InlineData("[FeedbackResolver] public static int Read(System.Span<int> value) => 0;", "HKFSG011")]
        [InlineData("[FeedbackResolver] public static unsafe int Read(int* value) => 0;", "HKFSG011")]
        public void UnsupportedDeclarationsReportActionableDiagnostics(string declaration, string diagnosticId)
        {
            var generation = Run("using HKFeedbackV2; public abstract partial class Values { " + declaration + " }");
            var diagnostic = Assert.Single(generation.Result.Diagnostics);
            Assert.Equal(diagnosticId, diagnostic.Id);
            Assert.True(diagnostic.Location.IsInSource);
            Assert.Empty(generation.Result.Results.SelectMany(result => result.GeneratedSources));
        }

        [Theory]
        [InlineData("public class Values { [FeedbackResolver] public static int Read() => 0; }", "HKFSG012")]
        [InlineData("public class Outer { public partial class Values { [FeedbackResolver] public static int Read() => 0; } }", "HKFSG012")]
        [InlineData("public partial class Values<T> { [FeedbackResolver] public static int Read() => 0; }", "HKFSG008")]
        [InlineData("public struct Values { [FeedbackResolver] public static int Read() => 0; }", "HKFSG008")]
        public void UnsupportedContainingTypesAreDiagnosed(string declaration, string diagnosticId)
        {
            Assert.Equal(diagnosticId, Assert.Single(Run("using HKFeedbackV2; " + declaration).Result.Diagnostics).Id);
        }

        [Theory]
        [InlineData("public partial class Resolvers { [FeedbackResolver] public static int Read() => 0; }")]
        [InlineData("public partial class Values { public static class Resolvers { } [FeedbackResolver] public static int Read() => 0; }")]
        [InlineData("public partial class Values { public int Resolvers; [FeedbackResolver] public static int Read() => 0; }")]
        [InlineData("public partial class Values { [FeedbackResolver] public static int Resolvers() => 0; }")]
        public void NamesConflictingWithTheResolversContainerAreDiagnosed(string declaration)
        {
            Assert.Equal("HKFSG010", Assert.Single(Run("using HKFeedbackV2; " + declaration).Result.Diagnostics).Id);
        }

        [Fact]
        public void NormalizedNameCollisionsAreDiagnosedWithoutSuppressingOtherResolvers()
        {
            var generation = Run("""
                using HKFeedbackV2;
                public partial class Values
                {
                    [FeedbackResolver] public static int Read(int value) => value;
                    [FeedbackResolver] public static int Read(string value) => value.Length;
                    [FeedbackResolver] public static int Read_int() => 0;
                }
                """);
            Assert.Equal(2, generation.Result.Diagnostics.Length);
            Assert.All(generation.Result.Diagnostics, diagnostic => Assert.Equal("HKFSG010", diagnostic.Id));
            var assembly = Emit(generation);
            Assert.NotNull(assembly.GetType("Values+Resolvers+Read_string"));
            Assert.Null(assembly.GetType("Values+Resolvers+Read_int"));
        }

        [Fact]
        public void UnrelatedAttributesAndUnannotatedMethodsAreIgnored()
        {
            var generation = Run("""
                using System;
                namespace Other;
                public class FeedbackResolverAttribute : Attribute { }
                public partial class Values
                {
                    [FeedbackResolver] public static int Read() => 0;
                    public static int Unannotated() => 0;
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
                public partial class Values
                {
                    [FeedbackResolver] private int Read(int value) => value;
                }
                """, enabled);
            AssertSuccess(generation);
            if (!enabled && typeof(IResolver<>).Assembly.GetType("SubclassSelectorAttribute") == null)
                Assert.Null(generation.Compilation.GetTypeByMetadataName("SubclassSelectorAttribute"));
            var fields = Emit(generation).GetType("Values+Resolvers+Read")!
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.Equal(2, fields.Length);
            foreach (var field in fields)
            {
                Assert.True(field.IsDefined(typeof(UnityEngine.SerializeReference)));
                Assert.Equal(enabled, field.GetCustomAttributes().Any(attribute => attribute.GetType().Name == "SubclassSelectorAttribute"));
            }
        }

        private static IResolver<T> CreateResolver<T>(Assembly assembly, string name) =>
            (IResolver<T>)Activator.CreateInstance(assembly.GetType(name, throwOnError: true)!)!;

        private static void SetField(object instance, string fieldName, object value) => instance.GetType()
            .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);

    }
}
