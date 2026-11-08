#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace HKFeedbackV2.SourceGenerator.Tests
{
    internal static class FeedbackGeneratorTestDriver
    {
        private static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.CSharp10);
        private static readonly ImmutableArray<MetadataReference> References =
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                .Append(typeof(IResolver<>).Assembly.Location)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => MetadataReference.CreateFromFile(path))
                .ToImmutableArray<MetadataReference>();

        internal static Generation Run(string source, bool enableSerializeReferenceExtensions = false)
        {
            var parseOptions = enableSerializeReferenceExtensions
                ? ParseOptions.WithPreprocessorSymbols("HK_FEEDBACK_V2_SERIALIZE_REFERENCE_EXTENSIONS")
                : ParseOptions;
            var syntaxTrees = new List<SyntaxTree> { CSharpSyntaxTree.ParseText(source, parseOptions) };
            if (enableSerializeReferenceExtensions && typeof(IResolver<>).Assembly.GetType("SubclassSelectorAttribute") == null)
            {
                syntaxTrees.Add(CSharpSyntaxTree.ParseText("""
                    [System.AttributeUsage(System.AttributeTargets.Field)]
                    public sealed class SubclassSelectorAttribute : System.Attribute { }
                    """, parseOptions));
            }
            var compilation = CSharpCompilation.Create("GeneratedFeedback_" + Guid.NewGuid().ToString("N"),
                syntaxTrees, References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new[]
            {
                new FeedbackActionGenerator().AsSourceGenerator(),
                new FeedbackResolverGenerator().AsSourceGenerator(),
                new FeedbackEvaluatorGenerator().AsSourceGenerator()
            }, parseOptions: parseOptions);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
            return new Generation(output, driver.GetRunResult(), diagnostics);
        }

        internal static void AssertSuccess(Generation generation)
        {
            Assert.Empty(generation.Diagnostics);
            Assert.Empty(generation.Result.Diagnostics);
            var errors = generation.Compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            Assert.True(!errors.Any(), string.Join(Environment.NewLine, errors));
        }

        internal static Assembly Compile(string source)
        {
            var generation = Run(source);
            AssertSuccess(generation);
            return Emit(generation);
        }

        internal static Assembly Emit(Generation generation)
        {
            using var stream = new MemoryStream();
            var result = generation.Compilation.Emit(stream);
            Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
            stream.Position = 0;
            return AssemblyLoadContext.Default.LoadFromStream(stream);
        }

        internal sealed record Generation(Compilation Compilation, GeneratorDriverRunResult Result,
            ImmutableArray<Diagnostic> Diagnostics);
    }
}
