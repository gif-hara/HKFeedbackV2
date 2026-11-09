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
    internal enum FeedbackKind
    {
        Action,
        Resolver,
        Evaluator
    }

    internal static class FeedbackGenerator
    {
        private const string ActionAttributeName = "HKFeedbackV2.FeedbackActionAttribute";
        private const string ResolverAttributeName = "HKFeedbackV2.FeedbackResolverAttribute";
        private const string EvaluatorAttributeName = "HKFeedbackV2.FeedbackEvaluatorAttribute";
        private const string ContainerName = "HKFeedbackV2.IContainer";
        private const string EvaluatorName = "HKFeedbackV2.IEvaluator";
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

        private static readonly DiagnosticDescriptor UnsupportedResolverMethod = Error(
            "HKFSG008", "Unsupported feedback resolver method",
            "Cannot generate a resolver for '{0}': {1}");
        private static readonly DiagnosticDescriptor UnsupportedResolverReturn = Error(
            "HKFSG009", "Unsupported feedback resolver return type",
            "'{0}' must return a value usable as IResolver<T>: void, pointer, ref-like, and open generic types are unsupported");
        private static readonly DiagnosticDescriptor DuplicateResolverName = Error(
            "HKFSG010", "Resolver class name already exists",
            "Cannot generate '{0}': its name conflicts with an existing member or another resolver; rename the conflicting member or method");
        private static readonly DiagnosticDescriptor UnsupportedResolverParameter = Error(
            "HKFSG011", "Unsupported feedback resolver parameter",
            "Cannot generate a resolver for parameter '{0}' on '{1}': ref, in, out, pointer, ref-like, and open generic types are unsupported");
        private static readonly DiagnosticDescriptor MissingResolverPartial = Error(
            "HKFSG012", "Feedback resolver containing class must be partial",
            "Class '{0}' must be declared partial to generate nested resolver classes");

        private static readonly DiagnosticDescriptor UnsupportedEvaluatorMethod = Error(
            "HKFSG013", "Unsupported feedback evaluator method",
            "Cannot generate an evaluator for '{0}': {1}");
        private static readonly DiagnosticDescriptor UnsupportedEvaluatorReturn = Error(
            "HKFSG014", "Unsupported feedback evaluator return type",
            "'{0}' must return bool");
        private static readonly DiagnosticDescriptor DuplicateEvaluatorName = Error(
            "HKFSG015", "Evaluator class name already exists",
            "Cannot generate '{0}': its name conflicts with an existing member or another evaluator; rename the conflicting member or method");
        private static readonly DiagnosticDescriptor UnsupportedEvaluatorParameter = Error(
            "HKFSG016", "Unsupported feedback evaluator parameter",
            "Cannot generate a resolver for parameter '{0}' on '{1}': ref, in, out, pointer, ref-like, and open generic types are unsupported");
        private static readonly DiagnosticDescriptor MissingEvaluatorPartial = Error(
            "HKFSG017", "Feedback evaluator containing class must be partial",
            "Class '{0}' must be declared partial to generate nested evaluator classes");

        private static string AttributeName(FeedbackKind kind) => kind switch
        {
            FeedbackKind.Resolver => ResolverAttributeName,
            FeedbackKind.Evaluator => EvaluatorAttributeName,
            _ => ActionAttributeName
        };

        private static string ContainerClassName(FeedbackKind kind) => kind switch
        {
            FeedbackKind.Resolver => "Resolvers",
            FeedbackKind.Evaluator => "Evaluators",
            _ => "Actions"
        };

        public static void Initialize(IncrementalGeneratorInitializationContext context, FeedbackKind kind)
        {
            var attributeName = AttributeName(kind);
            var methods = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is MethodDeclarationSyntax method && method.AttributeLists.Count != 0,
                (syntaxContext, cancellationToken) =>
                {
                    var method = syntaxContext.SemanticModel.GetDeclaredSymbol(
                        (MethodDeclarationSyntax)syntaxContext.Node, cancellationToken);
                    return method != null && method.GetAttributes().Any(a =>
                        a.AttributeClass?.ToDisplayString() == attributeName) ? method : null;
                }).Where(static method => method != null)
                .Select(static (method, _) => method!);

            context.RegisterSourceOutput(methods.Collect(),
                (output, collectedMethods) => Generate(output, collectedMethods, kind));
        }

        private static void Generate(SourceProductionContext context,
            ImmutableArray<IMethodSymbol> methods, FeedbackKind kind)
        {
            var candidates = new List<FeedbackDefinition>();
            var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            var reportedPartialClasses = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            var attributeName = AttributeName(kind);
            var containerClassName = ContainerClassName(kind);
            var (methodRule, returnRule, duplicateName, parameterRule, partialRule) = kind switch
            {
                FeedbackKind.Resolver => (UnsupportedResolverMethod, UnsupportedResolverReturn,
                    DuplicateResolverName, UnsupportedResolverParameter, MissingResolverPartial),
                FeedbackKind.Evaluator => (UnsupportedEvaluatorMethod, UnsupportedEvaluatorReturn,
                    DuplicateEvaluatorName, UnsupportedEvaluatorParameter, MissingEvaluatorPartial),
                _ => (UnsupportedMethod, UnsupportedReturn, DuplicateName, UnsupportedParameter, MissingPartial)
            };

            foreach (var method in methods)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                if (!seen.Add(method))
                {
                    continue;
                }

                var attribute = method.GetAttributes().First(a =>
                    a.AttributeClass?.ToDisplayString() == attributeName);
                var location = attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
                    ?? method.Locations.FirstOrDefault();

                var reason = ValidateMethod(method);
                if (reason != null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(methodRule, location,
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
                        context.ReportDiagnostic(Diagnostic.Create(partialRule,
                            type.Locations.FirstOrDefault() ?? location, type.ToDisplayString()));
                    }
                }
                if (missingPartial)
                {
                    continue;
                }

                if (kind == FeedbackKind.Action && method.IsAsync && method.ReturnsVoid)
                {
                    context.ReportDiagnostic(Diagnostic.Create(AsyncVoid, location, method.ToDisplayString()));
                    continue;
                }

                var returnType = method.ReturnType.ToDisplayString();
                var validReturn = kind switch
                {
                    FeedbackKind.Resolver => !method.ReturnsVoid && CanResolve(method.ReturnType),
                    FeedbackKind.Evaluator => method.ReturnType.SpecialType == SpecialType.System_Boolean,
                    _ => method.ReturnsVoid || returnType == UniTaskName || returnType == TaskName
                };
                if (!validReturn)
                {
                    context.ReportDiagnostic(Diagnostic.Create(returnRule, location, method.ToDisplayString()));
                    continue;
                }

                var invalidParameters = method.Parameters.Where(p =>
                    p.RefKind != RefKind.None || !CanResolve(p.Type)).ToArray();
                if (invalidParameters.Length != 0)
                {
                    foreach (var parameter in invalidParameters)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(parameterRule,
                            parameter.Locations.FirstOrDefault() ?? location,
                            parameter.Name, method.ToDisplayString()));
                    }
                    continue;
                }

                var definition = new FeedbackDefinition(method, location, kind);
                if (kind == FeedbackKind.Action && returnType == UniTaskName && definition.Name == "InvokeAsyncInternal")
                {
                    context.ReportDiagnostic(Diagnostic.Create(DuplicateName, location, definition.FullName));
                    continue;
                }
                candidates.Add(definition);
            }

            foreach (var group in candidates.GroupBy(c => TypeName(c.Method.ContainingType), StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                var owner = group.First().Method.ContainingType;
                if (owner.Name == containerClassName || owner.GetMembers(containerClassName).Any(member =>
                    !(member is INamedTypeSymbol type) || type.Arity == 0))
                {
                    context.ReportDiagnostic(Diagnostic.Create(duplicateName, group.First().Location,
                        owner.ToDisplayString() + "." + containerClassName));
                    continue;
                }

                var definitions = new List<FeedbackDefinition>();
                foreach (var names in group.GroupBy(definition => definition.Name, StringComparer.Ordinal)
                    .OrderBy(names => names.Key, StringComparer.Ordinal))
                {
                    if (names.Count() > 1)
                    {
                        foreach (var definition in names)
                        {
                            context.ReportDiagnostic(Diagnostic.Create(duplicateName, definition.Location, definition.FullName));
                        }
                    }
                    else
                    {
                        definitions.Add(names.First());
                    }
                }
                if (definitions.Count != 0)
                {
                    var hintName = group.Key.Substring("global::".Length).Replace("@", "") + "." + containerClassName + ".g.cs";
                    context.AddSource(hintName, SourceText.From(Render(owner, definitions, containerClassName), Encoding.UTF8));
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

        private static bool IsInjected(IParameterSymbol parameter, FeedbackKind kind)
        {
            var typeName = parameter.Type.ToDisplayString();
            return typeName == ContainerName || (kind == FeedbackKind.Action && typeName == CancellationTokenName);
        }

        private static bool IsDirectEvaluator(ITypeSymbol type, FeedbackKind kind) => kind == FeedbackKind.Action &&
            (type.ToDisplayString() == EvaluatorName ||
                type is IArrayTypeSymbol array && array.ElementType.ToDisplayString() == EvaluatorName);

        private static string GeneratedName(IMethodSymbol method, string attributeName) => method.ContainingType.GetMembers(method.Name)
            .OfType<IMethodSymbol>()
            .Count(candidate => candidate.GetAttributes().Any(attribute =>
                attribute.AttributeClass?.ToDisplayString() == attributeName)) <= 1
                ? method.Name
                : SignatureName(method);

        private static string SignatureName(IMethodSymbol method) => method.Parameters.Length == 0
            ? method.Name
            : method.Name + "_" + string.Join("_", method.Parameters.Select(parameter => ParameterTypeName(parameter.Type)));

        private static string ParameterTypeName(ITypeSymbol type)
        {
            if (type.TypeKind == TypeKind.Dynamic) return "object";
            var alias = type.SpecialType switch
            {
                SpecialType.System_Boolean => "bool",
                SpecialType.System_Byte => "byte",
                SpecialType.System_SByte => "sbyte",
                SpecialType.System_Int16 => "short",
                SpecialType.System_UInt16 => "ushort",
                SpecialType.System_Int32 => "int",
                SpecialType.System_UInt32 => "uint",
                SpecialType.System_Int64 => "long",
                SpecialType.System_UInt64 => "ulong",
                SpecialType.System_Char => "char",
                SpecialType.System_Single => "float",
                SpecialType.System_Double => "double",
                SpecialType.System_Decimal => "decimal",
                SpecialType.System_String => "string",
                SpecialType.System_Object => "object",
                SpecialType.System_IntPtr => "nint",
                SpecialType.System_UIntPtr => "nuint",
                _ => null
            };
            if (alias != null) return alias;
            if (type is IArrayTypeSymbol array)
            {
                return ParameterTypeName(array.ElementType) + "_array" + (array.Rank == 1 ? "" : array.Rank.ToString());
            }
            if (type is INamedTypeSymbol named)
            {
                if (named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
                {
                    return ParameterTypeName(named.TypeArguments[0]) + "_nullable";
                }
                var name = named.ContainingType == null
                    ? named.Name
                    : ParameterTypeName(named.ContainingType) + "_" + named.Name;
                if (named.TypeArguments.Length != 0)
                {
                    name += "_" + string.Join("_", named.TypeArguments.Select(ParameterTypeName));
                }
                return name;
            }
            throw new InvalidOperationException("Unsupported feedback parameter type: " + type.ToDisplayString());
        }

        private static string Render(INamedTypeSymbol owner, IEnumerable<FeedbackDefinition> definitions, string containerClassName)
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

            writer.Open("public static class " + containerClassName);
            foreach (var definition in definitions)
            {
                RenderFeedback(writer, definition);
            }
            writer.Close();
            foreach (var _ in ContainingTypes(owner)) writer.Close();
            if (!owner.ContainingNamespace.IsGlobalNamespace) writer.Close();
            return writer.ToString();
        }

        private static void RenderFeedback(SourceWriter writer, FeedbackDefinition definition)
        {
            var method = definition.Method;
            var returnTypeName = TypeName(method.ReturnType);
            var isAction = definition.Kind == FeedbackKind.Action;
            var isAsyncAction = isAction && method.ReturnType.ToDisplayString() == UniTaskName;
            var baseType = definition.Kind switch
            {
                FeedbackKind.Resolver => "global::HKFeedbackV2.IResolver<" + returnTypeName + ">",
                FeedbackKind.Evaluator => "global::HKFeedbackV2.IEvaluator",
                _ => isAsyncAction ? "global::HKFeedbackV2.Actions.AsyncAction" : "global::HKFeedbackV2.IAction"
            };
            writer.Line("[global::System.Serializable]");
            writer.Open("public sealed class @" + definition.Name + " : " + baseType);

            if (!method.IsStatic)
            {
                var owner = TypeName(method.ContainingType);
                RenderSerializeReferenceAttributes(writer);
                writer.Line($"private global::HKFeedbackV2.IResolver<{owner}> {definition.TargetFieldName} = new global::HKFeedbackV2.Resolvers.Anonymous<{owner}>();");
                writer.Line();
            }

            foreach (var parameter in method.Parameters.Where(p => !IsInjected(p, definition.Kind)))
            {
                var typeName = TypeName(parameter.Type);
                var fieldName = definition.ParameterFieldName(parameter);
                RenderSerializeReferenceAttributes(writer);
                writer.Line("[global::UnityEngine.InspectorName(" + SymbolDisplay.FormatLiteral(parameter.Name, true) + ")]");
                if (IsDirectEvaluator(parameter.Type, definition.Kind))
                {
                    var defaultValue = parameter.Type is IArrayTypeSymbol ? DefaultValue(parameter)
                        : "new global::HKFeedbackV2.Evaluators.Constant(true)";
                    writer.Line($"private {typeName} @{fieldName} = {defaultValue};");
                }
                else
                {
                    var useAnonymous = isAction && parameter.Type is INamedTypeSymbol named &&
                        named.TypeKind == TypeKind.Class && !named.IsAbstract && !named.IsStatic &&
                        named.SpecialType != SpecialType.System_String;
                    var useSerializeReference = isAction && (parameter.Type.TypeKind == TypeKind.Interface ||
                        parameter.Type is IArrayTypeSymbol array && array.ElementType.TypeKind == TypeKind.Interface);
                    var resolverType = useAnonymous ? "Anonymous" : useSerializeReference ? "SerializeReference" : "SerializeField";
                    var constructorArguments = useAnonymous ? "" : DefaultValue(parameter);
                    writer.Line($"private global::HKFeedbackV2.IResolver<{typeName}> @{fieldName} = new global::HKFeedbackV2.Resolvers.{resolverType}<{typeName}>({constructorArguments});");
                }
                writer.Line();
            }

            var executionMethod = definition.Kind switch
            {
                FeedbackKind.Resolver => definition.Name == "Resolve"
                    ? returnTypeName + " " + baseType + ".Resolve"
                    : "public " + returnTypeName + " Resolve",
                FeedbackKind.Evaluator => definition.Name == "Evaluate"
                    ? "bool " + baseType + ".Evaluate"
                    : "public bool Evaluate",
                _ => isAsyncAction
                    ? "protected override global::Cysharp.Threading.Tasks.UniTask InvokeAsyncInternal"
                    : definition.Name == "InvokeAsync"
                        ? "global::Cysharp.Threading.Tasks.UniTask global::HKFeedbackV2.IAction.InvokeAsync"
                        : "public global::Cysharp.Threading.Tasks.UniTask InvokeAsync"
            };
            writer.Open(executionMethod + (isAction
                ? "(global::HKFeedbackV2.IContainer container, global::System.Threading.CancellationToken cancellationToken)"
                : "(global::HKFeedbackV2.IContainer container)"));
            if (isAction) writer.Line("cancellationToken.ThrowIfCancellationRequested();");

            var arguments = string.Join(", ", method.Parameters.Select(p =>
            {
                var name = p.Type.ToDisplayString();
                return name == ContainerName ? "container"
                    : isAction && name == CancellationTokenName ? "cancellationToken"
                    : IsDirectEvaluator(p.Type, definition.Kind) ? "this.@" + definition.ParameterFieldName(p)
                    : "this.@" + definition.ParameterFieldName(p) + ".Resolve(container)";
            }));
            var receiver = method.IsStatic ? TypeName(method.ContainingType)
                : "this." + definition.TargetFieldName + ".Resolve(container)";
            var invocation = receiver + ".@" + method.Name + "(" + arguments + ")";

            if (method.ReturnsVoid)
            {
                writer.Line(invocation + ";");
                writer.Line("return global::Cysharp.Threading.Tasks.UniTask.CompletedTask;");
            }
            else if (!isAction || isAsyncAction)
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

        private static void RenderSerializeReferenceAttributes(SourceWriter writer)
        {
            writer.Line("[global::UnityEngine.SerializeReference]");
            writer.Line("#if HK_FEEDBACK_V2_SERIALIZE_REFERENCE_EXTENSIONS");
            writer.Line("[global::SubclassSelector]");
            writer.Line("#endif");
        }

        private static string TypeName(ITypeSymbol type) =>
            type.TypeKind == TypeKind.Dynamic ? "global::System.Object"
                : type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

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

        private sealed class FeedbackDefinition
        {
            private readonly Dictionary<int, string> parameterFields = new Dictionary<int, string>();

            public IMethodSymbol Method { get; }
            public FeedbackKind Kind { get; }
            public string Name { get; }
            public string TargetFieldName { get; }
            public Location? Location { get; }
            public string FullName => Method.ContainingType.ToDisplayString() + "." + ContainerClassName(Kind) + "." + Name;

            public FeedbackDefinition(IMethodSymbol method, Location? location, FeedbackKind kind)
            {
                Method = method;
                Kind = kind;
                Name = GeneratedName(method, AttributeName(kind));
                Location = location;

                var parameters = method.Parameters.Where(parameter => !IsInjected(parameter, kind)).ToArray();
                var reserved = new HashSet<string>(StringComparer.Ordinal)
                {
                    Name, "Equals", "GetHashCode", "GetType",
                    "ToString", "MemberwiseClone", "ReferenceEquals", "Finalize"
                };
                if (kind == FeedbackKind.Resolver) reserved.Add("Resolve");
                else if (kind == FeedbackKind.Evaluator) reserved.Add("Evaluate");
                else reserved.UnionWith(new[] { "InvokeAsync", "InvokeAsyncInternal" });
                if (kind == FeedbackKind.Action && method.ReturnType.ToDisplayString() == UniTaskName) reserved.Add("isWait");

                var occupied = new HashSet<string>(parameters.Select(parameter => parameter.Name), StringComparer.Ordinal);
                occupied.UnionWith(reserved);
                foreach (var parameter in parameters)
                {
                    var fieldName = parameter.Name;
                    if (reserved.Contains(fieldName))
                    {
                        do { fieldName = "_" + fieldName; } while (occupied.Contains(fieldName));
                    }
                    parameterFields.Add(parameter.Ordinal, fieldName);
                    occupied.Add(fieldName);
                }

                var targetFieldName = "_" + method.ContainingType.Name;
                while (occupied.Contains(targetFieldName))
                {
                    targetFieldName = "_" + targetFieldName;
                }
                TargetFieldName = targetFieldName;
            }

            public string ParameterFieldName(IParameterSymbol parameter) => parameterFields[parameter.Ordinal];
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
