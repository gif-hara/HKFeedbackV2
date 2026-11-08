using Microsoft.CodeAnalysis;

namespace HKFeedbackV2.SourceGenerator
{
    [Generator(LanguageNames.CSharp)]
    public sealed class FeedbackActionGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context) =>
            FeedbackGenerator.Initialize(context, isResolver: false);
    }

    [Generator(LanguageNames.CSharp)]
    public sealed class FeedbackResolverGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context) =>
            FeedbackGenerator.Initialize(context, isResolver: true);
    }
}
