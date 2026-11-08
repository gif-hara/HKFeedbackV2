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
    internal static class FeedbackGenerator
    {
        private const string ActionAttributeName = "HKFeedbackV2.FeedbackActionAttribute";
        private const string ResolverAttributeName = "HKFeedbackV2.FeedbackResolverAttribute";
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

        public static void Initialize(IncrementalGeneratorInitializationContext context, bool isResolver)
        {
            var attributeName = isResolver ? ResolverAttributeName : ActionAttributeName;
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
                (output, collectedMethods) => Generate(output, collectedMethods, isResolver));
        }

        private static void Generate(SourceProductionContext context,
            ImmutableArray<IMethodSymbol> methods, bool isResolver)
        {
            var candidates = new List<FeedbackDefinition>();
            var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            var reportedPartialClasses = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            var attributeName = isResolver ? ResolverAttributeName : ActionAttributeName;
            var duplicateName = isResolver ? DuplicateResolverName : DuplicateName;
            var containerClassName = isResolver ? "Resolvers" : "Actions";

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
                    context.ReportDiagnostic(Diagnostic.Create(isResolver ? UnsupportedResolverMethod : UnsupportedMethod, location,
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
                        context.ReportDiagnostic(Diagnostic.Create(isResolver ? MissingResolverPartial : MissingPartial,
                            type.Locations.FirstOrDefault() ?? location, type.ToDisplayString()));
                    }
                }
                if (missingPartial)
                {
                    continue;
                }

                if (!isResolver && method.IsAsync && method.ReturnsVoid)
                {
                    context.ReportDiagnostic(Diagnostic.Create(AsyncVoid, location, method.ToDisplayString()));
                    continue;
                }

                var returnType = method.ReturnType.ToDisplayString();
                if (isResolver ? method.ReturnsVoid || !CanResolve(method.ReturnType)
                    : !method.ReturnsVoid && returnType != UniTaskName && returnType != TaskName)
                {
                    context.ReportDiagnostic(Diagnostic.Create(isResolver ? UnsupportedResolverReturn : UnsupportedReturn,
                        location, method.ToDisplayString()));
                    continue;
                }

                var invalidParameters = method.Parameters.Where(p =>
                    p.RefKind != RefKind.None || !CanResolve(p.Type)).ToArray();
                if (invalidParameters.Length != 0)
                {
                    foreach (var parameter in invalidParameters)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(isResolver ? UnsupportedResolverParameter : UnsupportedParameter,
                            parameter.Locations.FirstOrDefault() ?? location,
                            parameter.Name, method.ToDisplayString()));
                    }
                    continue;
                }

                var action = new FeedbackDefinition(method, location, isResolver);
                if (!isResolver && returnType == UniTaskName && action.Name == "InvokeAsyncInternal")
                {
                    context.ReportDiagnostic(Diagnostic.Create(DuplicateName, location, action.FullName));
                    continue;
                }
                candidates.Add(action);
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

                var actions = new List<FeedbackDefinition>();
                foreach (var names in group.GroupBy(action => action.Name, StringComparer.Ordinal)
                    .OrderBy(names => names.Key, StringComparer.Ordinal))
                {
                    if (names.Count() > 1)
                    {
                        foreach (var action in names)
                        {
                            context.ReportDiagnostic(Diagnostic.Create(duplicateName, action.Location, action.FullName));
                        }
                    }
                    else
                    {
                        actions.Add(names.First());
                    }
                }
                if (actions.Count != 0)
                {
                    var hintName = group.Key.Substring("global::".Length).Replace("@", "") + "." + containerClassName + ".g.cs";
                    context.AddSource(hintName, SourceText.From(Render(owner, actions, containerClassName), Encoding.UTF8));
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

        private static bool IsInjected(IParameterSymbol parameter, bool isResolver)
        {
            var typeName = parameter.Type.ToDisplayString();
            return typeName == ContainerName || (!isResolver && typeName == CancellationTokenName);
        }

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

        private static string Render(INamedTypeSymbol owner, IEnumerable<FeedbackDefinition> actions, string containerClassName)
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
            var actionNames = new HashSet<string>(actions.Select(action => action.Name), StringComparer.Ordinal);
            foreach (var action in actions)
            {
                var previousName = SignatureName(action.Method);
                RenderFeedback(writer, action, !action.IsResolver && action.Name != previousName && !actionNames.Contains(previousName)
                    ? string.Join("/", ContainingTypes(owner).Select(type => type.Name)) + "/Actions/" + previousName
                    : null);
            }
            writer.Close();
            foreach (var _ in ContainingTypes(owner)) writer.Close();
            if (!owner.ContainingNamespace.IsGlobalNamespace) writer.Close();
            return writer.ToString();
        }

        private static void RenderFeedback(SourceWriter writer, FeedbackDefinition action, string? previousClassName)
        {
            var method = action.Method;
            var returnTypeName = TypeName(method.ReturnType);
            var isAsyncAction = method.ReturnType.ToDisplayString() == UniTaskName;
            var baseType = action.IsResolver ? "global::HKFeedbackV2.IResolver<" + returnTypeName + ">"
                : isAsyncAction ? "global::HKFeedbackV2.Actions.AsyncAction" : "global::HKFeedbackV2.IAction";
            writer.Line("[global::System.Serializable]");
            if (previousClassName != null)
            {
                var owner = method.ContainingType;
                var sourceNamespace = owner.ContainingNamespace.IsGlobalNamespace ? ""
                    : owner.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat
                        .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.None));
                writer.Line("[global::UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: " +
                    SymbolDisplay.FormatLiteral(sourceNamespace, true) + ", sourceAssembly: " +
                    SymbolDisplay.FormatLiteral(owner.ContainingAssembly.Name, true) + ", sourceClassName: " +
                    SymbolDisplay.FormatLiteral(previousClassName, true) + ")]");
            }
            writer.Open("public sealed class @" + action.Name + " : " + baseType);

            if (!method.IsStatic)
            {
                var owner = TypeName(method.ContainingType);
                RenderSerializeReferenceAttributes(writer);
                writer.Line($"private global::HKFeedbackV2.IResolver<{owner}> {action.TargetFieldName} = new global::HKFeedbackV2.Resolvers.Anonymous<{owner}>();");
                writer.Line();
            }

            foreach (var parameter in method.Parameters.Where(p => !IsInjected(p, action.IsResolver)))
            {
                var typeName = TypeName(parameter.Type);
                var fieldName = action.ParameterFieldName(parameter);
                RenderSerializeReferenceAttributes(writer);
                writer.Line("[global::UnityEngine.InspectorName(" + SymbolDisplay.FormatLiteral(parameter.Name, true) + ")]");
                writer.Line($"private global::HKFeedbackV2.IResolver<{typeName}> @{fieldName} = new global::HKFeedbackV2.Resolvers.Embed<{typeName}>({DefaultValue(parameter)});");
                writer.Line();
            }

            var invokeMethod = action.IsResolver
                ? action.Name == "Resolve"
                    ? returnTypeName + " " + baseType + ".Resolve"
                    : "public " + returnTypeName + " Resolve"
                : isAsyncAction
                ? "protected override global::Cysharp.Threading.Tasks.UniTask InvokeAsyncInternal"
                : action.Name == "InvokeAsync"
                    ? "global::Cysharp.Threading.Tasks.UniTask global::HKFeedbackV2.IAction.InvokeAsync"
                    : "public global::Cysharp.Threading.Tasks.UniTask InvokeAsync";
            writer.Open(invokeMethod + (action.IsResolver
                ? "(global::HKFeedbackV2.IContainer container)"
                : "(global::HKFeedbackV2.IContainer container, global::System.Threading.CancellationToken cancellationToken)"));
            if (!action.IsResolver) writer.Line("cancellationToken.ThrowIfCancellationRequested();");

            var arguments = string.Join(", ", method.Parameters.Select(p =>
            {
                var name = p.Type.ToDisplayString();
                return name == ContainerName ? "container"
                    : !action.IsResolver && name == CancellationTokenName ? "cancellationToken"
                    : "this.@" + action.ParameterFieldName(p) + ".Resolve(container)";
            }));
            var receiver = method.IsStatic ? TypeName(method.ContainingType)
                : "this." + action.TargetFieldName + ".Resolve(container)";
            var invocation = receiver + ".@" + method.Name + "(" + arguments + ")";

            if (method.ReturnsVoid)
            {
                writer.Line(invocation + ";");
                writer.Line("return global::Cysharp.Threading.Tasks.UniTask.CompletedTask;");
            }
            else if (action.IsResolver || isAsyncAction)
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

        private static string FieldName(string className, string name) => className == name ? "_" + name : name;

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
            public bool IsResolver { get; }
            public string Name { get; }
            public string TargetFieldName => FieldName(Name, "_target");
            public Location? Location { get; }
            public string FullName => Method.ContainingType.ToDisplayString() +
                (IsResolver ? ".Resolvers." : ".Actions.") + Name;

            public FeedbackDefinition(IMethodSymbol method, Location? location, bool isResolver)
            {
                Method = method;
                IsResolver = isResolver;
                Name = GeneratedName(method, isResolver ? ResolverAttributeName : ActionAttributeName);
                Location = location;

                var parameters = method.Parameters.Where(parameter => !IsInjected(parameter, isResolver)).ToArray();
                var reserved = new HashSet<string>(StringComparer.Ordinal)
                {
                    Name, "Equals", "GetHashCode", "GetType",
                    "ToString", "MemberwiseClone", "ReferenceEquals", "Finalize"
                };
                if (isResolver) reserved.Add("Resolve");
                else reserved.UnionWith(new[] { "InvokeAsync", "InvokeAsyncInternal" });
                if (!method.IsStatic) reserved.Add(TargetFieldName);
                if (!isResolver && method.ReturnType.ToDisplayString() == UniTaskName) reserved.Add("isWait");

                var legacyNames = isResolver ? new Dictionary<int, string>()
                    : parameters.ToDictionary(parameter => parameter.Ordinal, LegacyParameterFieldName);
                var occupied = new HashSet<string>(parameters.Select(parameter => parameter.Name), StringComparer.Ordinal);
                occupied.UnionWith(reserved);
                occupied.UnionWith(legacyNames.Values);
                foreach (var parameter in parameters)
                {
                    var fieldName = parameter.Name;
                    if (reserved.Contains(fieldName) || legacyNames.Any(legacy =>
                        legacy.Key != parameter.Ordinal && legacy.Value == fieldName))
                    {
                        do { fieldName = "_" + fieldName; } while (occupied.Contains(fieldName));
                    }
                    parameterFields.Add(parameter.Ordinal, fieldName);
                    occupied.Add(fieldName);
                }
            }

            public string ParameterFieldName(IParameterSymbol parameter) => parameterFields[parameter.Ordinal];

            public string LegacyParameterFieldName(IParameterSymbol parameter) => FieldName(Name, "_argument" + parameter.Ordinal);
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
