using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace HKFeedbackV2.SourceGenerator
{
    [Generator(LanguageNames.CSharp)]
    public sealed class FeedbackActionGenerator : IIncrementalGenerator
    {
        private const string AttributeName = "HKFeedbackV2.FeedbackActionAttribute";
        private const string ContainerName = "HKFeedbackV2.IContainer";
        private const string CancellationTokenName = "System.Threading.CancellationToken";
        private const string UniTaskName = "Cysharp.Threading.Tasks.UniTask";
        private const string TaskName = "System.Threading.Tasks.Task";

        private static readonly DiagnosticDescriptor UnsupportedMethod = Error(
            "HKFSG002", "Unsupported feedback action method",
            "Cannot generate an action for '{0}': {1}");
        private static readonly DiagnosticDescriptor UnsupportedReturn = Error(
            "HKFSG003", "Unsupported feedback action return type",
            "'{0}' must return void, UniTask, or Task");
        private static readonly DiagnosticDescriptor DuplicateName = Error(
            "HKFSG004", "Action class name already exists",
            "Cannot generate '{0}': its name conflicts with an existing member or another action; rename the conflicting member or method");
        private static readonly DiagnosticDescriptor UnsupportedParameter = Error(
            "HKFSG005", "Unsupported feedback action parameter",
            "Cannot generate a resolver for parameter '{0}' on '{1}': ref, in, out, pointer, ref-like, and open generic types are unsupported");
        private static readonly DiagnosticDescriptor AsyncVoid = Error(
            "HKFSG006", "Async void feedback action cannot be awaited",
            "'{0}' is async void; return UniTask or Task so the action can await completion");
        private static readonly DiagnosticDescriptor MissingPartial = Error(
            "HKFSG007", "Feedback action containing class must be partial",
            "Class '{0}' must be declared partial to generate nested action classes");

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var methods = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is MethodDeclarationSyntax method && method.AttributeLists.Count != 0,
                static (syntaxContext, cancellationToken) =>
                {
                    var method = syntaxContext.SemanticModel.GetDeclaredSymbol(
                        (MethodDeclarationSyntax)syntaxContext.Node, cancellationToken);
                    return method != null && method.GetAttributes().Any(a =>
                        a.AttributeClass?.ToDisplayString() == AttributeName) ? method : null;
                }).Where(static method => method != null)
                .Select(static (method, _) => method!);

            context.RegisterSourceOutput(methods.Collect(),
                static (output, methods) => Generate(output, methods));
        }

        private static void Generate(SourceProductionContext context,
            ImmutableArray<IMethodSymbol> methods)
        {
            var candidates = new List<ActionDefinition>();
            var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            var reportedPartialClasses = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

            foreach (var method in methods)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                if (!seen.Add(method))
                {
                    continue;
                }

                var attribute = method.GetAttributes().First(a =>
                    a.AttributeClass?.ToDisplayString() == AttributeName);
                var location = attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
                    ?? method.Locations.FirstOrDefault();

                var reason = ValidateMethod(method);
                if (reason != null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(UnsupportedMethod, location,
                        method.ToDisplayString(), reason));
                    continue;
                }

                var missingPartial = false;
                foreach (var type in ContainingTypes(method.ContainingType))
                {
                    if (type.DeclaringSyntaxReferences.All(reference =>
                        reference.GetSyntax(context.CancellationToken) is TypeDeclarationSyntax declaration &&
                        declaration.Modifiers.Any(SyntaxKind.PartialKeyword)))
                    {
                        continue;
                    }
                    missingPartial = true;
                    if (reportedPartialClasses.Add(type))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(MissingPartial,
                            type.Locations.FirstOrDefault() ?? location, type.ToDisplayString()));
                    }
                }
                if (missingPartial)
                {
                    continue;
                }

                if (method.IsAsync && method.ReturnsVoid)
                {
                    context.ReportDiagnostic(Diagnostic.Create(AsyncVoid, location, method.ToDisplayString()));
                    continue;
                }

                var returnType = method.ReturnType.ToDisplayString();
                if (!method.ReturnsVoid && returnType != UniTaskName && returnType != TaskName)
                {
                    context.ReportDiagnostic(Diagnostic.Create(UnsupportedReturn, location, method.ToDisplayString()));
                    continue;
                }

                var invalidParameters = method.Parameters.Where(p =>
                    p.RefKind != RefKind.None || !CanResolve(p.Type)).ToArray();
                if (invalidParameters.Length != 0)
                {
                    foreach (var parameter in invalidParameters)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(UnsupportedParameter,
                            parameter.Locations.FirstOrDefault() ?? location,
                            parameter.Name, method.ToDisplayString()));
                    }
                    continue;
                }

                candidates.Add(new ActionDefinition(method, location));
            }

            foreach (var group in candidates.GroupBy(c => TypeName(c.Method.ContainingType), StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                var owner = group.First().Method.ContainingType;
                if (owner.Name == "Actions" || owner.GetMembers("Actions").Any(member =>
                    !(member is INamedTypeSymbol type) || type.Arity == 0))
                {
                    context.ReportDiagnostic(Diagnostic.Create(DuplicateName, group.First().Location,
                        owner.ToDisplayString() + ".Actions"));
                    continue;
                }

                var actions = new List<ActionDefinition>();
                foreach (var names in group.GroupBy(action => action.Method.Name, StringComparer.Ordinal)
                    .OrderBy(names => names.Key, StringComparer.Ordinal))
                {
                    if (names.Count() > 1)
                    {
                        foreach (var action in names)
                        {
                            context.ReportDiagnostic(Diagnostic.Create(DuplicateName, action.Location, action.FullName));
                        }
                    }
                    else
                    {
                        actions.Add(names.First());
                    }
                }
                if (actions.Count != 0)
                {
                    var hintName = group.Key.Substring("global::".Length).Replace("@", "") + ".Actions.g.cs";
                    context.AddSource(hintName, SourceText.From(Render(owner, actions), Encoding.UTF8));
                }
            }
        }

        private static string? ValidateMethod(IMethodSymbol method)
        {
            if (method.IsGenericMethod)
            {
                return "generic methods are unsupported";
            }
            for (var type = method.ContainingType; type != null; type = type.ContainingType)
            {
                if (type.TypeKind != TypeKind.Class)
                {
                    return "the method and all containing types must belong to classes";
                }
                if (type.Arity != 0)
                {
                    return "generic containing types are unsupported";
                }
            }
            if (method.IsAbstract || method.IsExtern ||
                (method.IsPartialDefinition && method.PartialImplementationPart == null) ||
                method.MethodKind != MethodKind.Ordinary)
            {
                return "a callable, implemented ordinary method is required";
            }
            if (method.ReturnsByRef || method.ReturnsByRefReadonly)
            {
                return "ref returns are unsupported";
            }
            return null;
        }

        private static bool CanResolve(ITypeSymbol type)
        {
            if (type.TypeKind == TypeKind.Pointer || type.TypeKind == TypeKind.FunctionPointer ||
                type.TypeKind == TypeKind.TypeParameter || type.TypeKind == TypeKind.Error || type.IsRefLikeType)
            {
                return false;
            }
            if (type is IArrayTypeSymbol array)
            {
                return CanResolve(array.ElementType);
            }
            return !(type is INamedTypeSymbol named) || named.TypeArguments.All(CanResolve);
        }

        private static IEnumerable<INamedTypeSymbol> ContainingTypes(INamedTypeSymbol owner)
        {
            var types = new Stack<INamedTypeSymbol>();
            for (var type = owner; type != null; type = type.ContainingType)
            {
                types.Push(type);
            }
            return types;
        }

        private static bool IsInjected(IParameterSymbol parameter)
        {
            var typeName = parameter.Type.ToDisplayString();
            return typeName == ContainerName || typeName == CancellationTokenName;
        }

        private static string Render(INamedTypeSymbol owner, IEnumerable<ActionDefinition> actions)
        {
            var writer = new SourceWriter();
            writer.Line("// <auto-generated/>");
            writer.Line("#nullable disable");
            if (!owner.ContainingNamespace.IsGlobalNamespace)
            {
                var namespaceName = owner.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    .Replace("global::", "");
                writer.Open("namespace " + namespaceName);
            }

            foreach (var type in ContainingTypes(owner))
            {
                var accessibility = type.DeclaredAccessibility switch
                {
                    Accessibility.Public => "public ",
                    Accessibility.Internal => "internal ",
                    Accessibility.Private => "private ",
                    Accessibility.Protected => "protected ",
                    Accessibility.ProtectedOrInternal => "protected internal ",
                    Accessibility.ProtectedAndInternal => "private protected ",
                    _ => ""
                };
                var modifier = type.IsStatic ? "static " : "";
                var kind = type.IsRecord ? "record class " : "class ";
                writer.Open(accessibility + modifier + "partial " + kind + "@" + type.Name);
            }

            writer.Open("public static class Actions");
            foreach (var action in actions)
            {
                RenderAction(writer, action.Method);
            }
            writer.Close();
            foreach (var _ in ContainingTypes(owner)) writer.Close();
            if (!owner.ContainingNamespace.IsGlobalNamespace) writer.Close();
            return writer.ToString();
        }

        private static void RenderAction(SourceWriter writer, IMethodSymbol method)
        {
            writer.Line("[global::System.Serializable]");
            writer.Open("public sealed class @" + method.Name + " : global::HKFeedbackV2.IAction");

            if (!method.IsStatic)
            {
                var owner = TypeName(method.ContainingType);
                writer.Line("[global::UnityEngine.SerializeReference, global::SubclassSelector]");
                writer.Line($"private global::HKFeedbackV2.IResolver<{owner}> {FieldName(method, "_target")} = new global::HKFeedbackV2.Resolvers.ContainerAnonymous<{owner}>();");
                writer.Line();
            }

            foreach (var parameter in method.Parameters.Where(p => !IsInjected(p)))
            {
                var typeName = TypeName(parameter.Type);
                writer.Line("[global::UnityEngine.SerializeReference, global::SubclassSelector]");
                writer.Line("[global::UnityEngine.InspectorName(" + SymbolDisplay.FormatLiteral(parameter.Name, true) + ")]");
                writer.Line($"private global::HKFeedbackV2.IResolver<{typeName}> {FieldName(method, "_argument" + parameter.Ordinal)} = new global::HKFeedbackV2.Resolvers.Constant<{typeName}>({DefaultValue(parameter)});");
                writer.Line();
            }

            var invokeMethod = method.Name == "InvokeAsync"
                ? "global::Cysharp.Threading.Tasks.UniTask global::HKFeedbackV2.IAction.InvokeAsync"
                : "public global::Cysharp.Threading.Tasks.UniTask InvokeAsync";
            writer.Open(invokeMethod + "(global::HKFeedbackV2.IContainer container, global::System.Threading.CancellationToken cancellationToken)");
            writer.Line("cancellationToken.ThrowIfCancellationRequested();");

            var arguments = string.Join(", ", method.Parameters.Select(p =>
            {
                var name = p.Type.ToDisplayString();
                return name == ContainerName ? "container"
                    : name == CancellationTokenName ? "cancellationToken"
                    : "this." + FieldName(method, "_argument" + p.Ordinal) + ".Resolve(container)";
            }));
            var receiver = method.IsStatic ? TypeName(method.ContainingType)
                : "this." + FieldName(method, "_target") + ".Resolve(container)";
            var invocation = receiver + ".@" + method.Name + "(" + arguments + ")";

            if (method.ReturnsVoid)
            {
                writer.Line(invocation + ";");
                writer.Line("return global::Cysharp.Threading.Tasks.UniTask.CompletedTask;");
            }
            else if (method.ReturnType.ToDisplayString() == UniTaskName)
            {
                writer.Line("return " + invocation + ";");
            }
            else
            {
                writer.Line("return global::Cysharp.Threading.Tasks.UniTaskExtensions.AsUniTask(" + invocation + ");");
            }
            writer.Close();
            writer.Close();
            writer.Line();
        }

        private static string FieldName(IMethodSymbol method, string name) => method.Name == name ? "_" + name : name;

        private static string TypeName(ITypeSymbol type) =>
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        private static string DefaultValue(IParameterSymbol parameter)
        {
            var typeName = TypeName(parameter.Type);
            if (!parameter.HasExplicitDefaultValue || parameter.ExplicitDefaultValue == null)
            {
                return "default(" + typeName + ")";
            }
            if (parameter.ExplicitDefaultValue is float single)
            {
                if (float.IsNaN(single)) return "global::System.Single.NaN";
                if (float.IsPositiveInfinity(single)) return "global::System.Single.PositiveInfinity";
                if (float.IsNegativeInfinity(single)) return "global::System.Single.NegativeInfinity";
            }
            if (parameter.ExplicitDefaultValue is double number)
            {
                if (double.IsNaN(number)) return "global::System.Double.NaN";
                if (double.IsPositiveInfinity(number)) return "global::System.Double.PositiveInfinity";
                if (double.IsNegativeInfinity(number)) return "global::System.Double.NegativeInfinity";
            }
            var literal = SymbolDisplay.FormatPrimitive(parameter.ExplicitDefaultValue,
                quoteStrings: true, useHexadecimalNumbers: false);
            return "(" + typeName + ")(" + literal + ")";
        }

        private static DiagnosticDescriptor Error(string id, string title, string message) =>
            new DiagnosticDescriptor(id, title, message, "HKFeedbackV2.SourceGenerator",
                DiagnosticSeverity.Error, isEnabledByDefault: true);

        private sealed class ActionDefinition
        {
            public IMethodSymbol Method { get; }
            public Location? Location { get; }
            public string FullName => Method.ContainingType.ToDisplayString() + ".Actions." + Method.Name;

            public ActionDefinition(IMethodSymbol method, Location? location)
            {
                Method = method;
                Location = location;
            }
        }

        private sealed class SourceWriter
        {
            private readonly StringBuilder builder = new StringBuilder();
            private int indent;

            public void Line(string line = "")
            {
                if (line.Length != 0) builder.Append(' ', indent * 4).Append(line);
                builder.Append('\n');
            }

            public void Open(string declaration)
            {
                Line(declaration);
                Line("{");
                indent++;
            }

            public void Close()
            {
                indent--;
                Line("}");
            }

            public override string ToString() => builder.ToString();
        }
    }
}
