using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace LiteDB.Generator
{
    [Generator]
    public sealed class LiteDbGenerator : IIncrementalGenerator
    {
        private const string EntityAttributeName = "LiteDB.LiteEntityAttribute";
        private const string IdAttributeName = "LiteDB.BsonIdAttribute";
        private const string FieldAttributeName = "LiteDB.BsonFieldAttribute";
        private const string IgnoreAttributeName = "LiteDB.BsonIgnoreAttribute";
        private const string DiscriminatorAttributeName = "LiteDB.LiteDiscriminatorAttribute";

        private static readonly DiagnosticDescriptor OpenHierarchyCase = new DiagnosticDescriptor(
            "LDBGEN001",
            "Closed hierarchy contains an open case",
            "Case '{0}' must be sealed or closed for a generated LiteDB hierarchy contract",
            "LiteDB.Generator",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor MissingDiscriminator = new DiagnosticDescriptor(
            "LDBGEN002",
            "Closed hierarchy case has no discriminator",
            "Case '{0}' must declare LiteDiscriminatorAttribute for a generated LiteDB hierarchy contract",
            "LiteDB.Generator",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterPostInitializationOutput(static output => output.AddSource(
                "LiteEntityAttribute.g.cs",
                SourceText.From(
                    "namespace LiteDB { [global::System.AttributeUsage(global::System.AttributeTargets.Class, Inherited = false)] internal sealed class LiteEntityAttribute : global::System.Attribute { } }",
                    Encoding.UTF8)));

            context.RegisterPostInitializationOutput(static output => output.AddSource(
                "LiteDiscriminatorAttribute.g.cs",
                SourceText.From(
                    "namespace LiteDB { [global::System.AttributeUsage(global::System.AttributeTargets.Class, Inherited = false)] internal sealed class LiteDiscriminatorAttribute : global::System.Attribute { public LiteDiscriminatorAttribute(string value) { Value = value; } public string Value { get; } } }",
                    Encoding.UTF8)));

            var entities = context.SyntaxProvider.ForAttributeWithMetadataName(
                EntityAttributeName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (attributeContext, _) => CreateModel((INamedTypeSymbol)attributeContext.TargetSymbol));

            var reachableModels = entities.Collect().SelectMany(static (models, _) => ExpandReachableModels(models));
            var hierarchies = reachableModels.Collect().SelectMany(static (models, _) => FindHierarchies(models));

            context.RegisterSourceOutput(reachableModels, static (output, model) =>
                output.AddSource(model.HintName, SourceText.From(EmitEntity(model), Encoding.UTF8)));

            context.RegisterSourceOutput(reachableModels.Collect().Combine(hierarchies.Collect()), static (output, pair) =>
                output.AddSource("LiteDbGeneratedMapperProvider.g.cs", SourceText.From(EmitProvider(pair.Left, pair.Right), Encoding.UTF8)));

            context.RegisterSourceOutput(hierarchies, static (output, hierarchy) =>
            {
                foreach (var diagnostic in hierarchy.Diagnostics)
                {
                    output.ReportDiagnostic(Diagnostic.Create(diagnostic.Descriptor, diagnostic.Location, diagnostic.Arguments));
                }

                output.AddSource(hierarchy.HintName, SourceText.From(EmitHierarchy(hierarchy), Encoding.UTF8));
            });
        }

        private static EntityModel CreateModel(INamedTypeSymbol type)
        {
            var members = new List<MemberModel>();
            var typeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            foreach (var property in type.GetMembers().OfType<IPropertySymbol>()
                .Where(static property =>
                    !property.IsStatic &&
                    !property.IsIndexer &&
                    property.DeclaredAccessibility == Accessibility.Public &&
                    property.GetMethod != null)
                .OrderBy(static property => property.Name, StringComparer.Ordinal))
            {
                var attributes = property.GetAttributes();
                if (HasAttribute(attributes, IgnoreAttributeName)) continue;

                var fieldName = HasAttribute(attributes, IdAttributeName)
                    ? "_id"
                    : GetFieldName(attributes) ?? property.Name;
                var autoId = GetAutoId(attributes);
                var propertyType = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var propertyName = property.Name;
                var getter = $"target => (({typeName})target).{propertyName}";
                var setter = property.SetMethod == null || property.SetMethod.IsInitOnly
                    ? "null"
                    : $"(target, value) => (({typeName})target).{propertyName} = ({propertyType})value";

                members.Add(new MemberModel(
                    propertyName,
                    propertyType,
                    fieldName,
                    property.Type,
                    $@"mapper.Members.Add(new global::LiteDB.MemberMapper
{{
    AutoId = {autoId.ToString().ToLowerInvariant()},
    MemberName = {Literal(propertyName)},
    DataType = typeof({propertyType}),
    FieldName = {Literal(fieldName)},
    Getter = {getter},
    Setter = {setter}
}});"));
            }

            var constructor = SelectConstructor(type, members);

            var namespaceName = type.ContainingNamespace.IsGlobalNamespace
                ? null
                : type.ContainingNamespace.ToDisplayString();
            var hintName = type.ToDisplayString()
                .Replace("global::", string.Empty)
                .Replace('<', '_')
                .Replace('>', '_')
                .Replace('.', '_') + ".LiteDbMapper.g.cs";

            return new EntityModel(
                namespaceName,
                type.Name,
                typeName,
                hintName,
                string.Join("\n", members.Select(member => member.Initializer)),
                constructor,
                type,
                members);
        }

        private static IEnumerable<EntityModel> ExpandReachableModels(IReadOnlyList<EntityModel> roots)
        {
            var pending = new Queue<INamedTypeSymbol>();
            var visited = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            var rootModels = roots.ToDictionary(model => model.TypeSymbol, SymbolEqualityComparer.Default);

            foreach (var root in roots)
            {
                pending.Enqueue(root.TypeSymbol);
            }

            while (pending.Count > 0)
            {
                var type = pending.Dequeue();
                if (!visited.Add(type)) continue;

                var model = rootModels.TryGetValue(type, out var rootModel)
                    ? rootModel
                    : CreateModel(type);
                yield return model;

                foreach (var member in model.Members)
                {
                    var reachable = GetReachableType(member.TypeSymbol);
                    if (reachable != null && IsLocalComplexType(reachable, type))
                    {
                        pending.Enqueue(reachable);
                    }

                    if (member.TypeSymbol is INamedTypeSymbol named && IsClosedHierarchy(named))
                    {
                        foreach (var hierarchyCase in GetHierarchyCases(named))
                        {
                            pending.Enqueue(hierarchyCase);
                        }
                    }
                }
            }
        }

        private static INamedTypeSymbol? GetReachableType(ITypeSymbol type)
        {
            if (type is IArrayTypeSymbol array) return GetReachableType(array.ElementType);
            if (type is not INamedTypeSymbol named) return null;
            if (named.SpecialType != SpecialType.None) return null;

            var dictionary = named.AllInterfaces.FirstOrDefault(interfaceType =>
                interfaceType.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IDictionary<TKey, TValue>");
            if (dictionary != null) return GetReachableType(dictionary.TypeArguments[1]);

            var enumerable = named.AllInterfaces.FirstOrDefault(interfaceType =>
                interfaceType.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>");
            if (enumerable != null) return GetReachableType(enumerable.TypeArguments[0]);

            return named.TypeKind == TypeKind.Class ? named : null;
        }

        private static bool IsLocalComplexType(INamedTypeSymbol type, INamedTypeSymbol root)
        {
            return type.TypeKind == TypeKind.Class &&
                SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, root.ContainingAssembly);
        }

        private static IEnumerable<HierarchyModel> FindHierarchies(IReadOnlyList<EntityModel> models)
        {
            var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

            foreach (var model in models)
            {
                foreach (var member in model.Members)
                {
                    if (member.TypeSymbol is not INamedTypeSymbol type || !IsClosedHierarchy(type) || !seen.Add(type))
                    {
                        continue;
                    }

                    var cases = GetHierarchyCases(type).Select(CreateHierarchyCase).ToArray();
                    var diagnostics = cases
                        .Where(item => item.Diagnostic != null)
                        .Where(item => item.Diagnostic.HasValue)
                        .Select(item => item.Diagnostic.GetValueOrDefault())
                        .ToArray();

                    yield return new HierarchyModel(type, cases, diagnostics);
                }
            }
        }

        private static HierarchyCaseModel CreateHierarchyCase(INamedTypeSymbol type)
        {
            var discriminator = type.GetAttributes()
                .FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString() == DiscriminatorAttributeName)?
                .ConstructorArguments.FirstOrDefault().Value as string;
            var isClosed = IsClosedHierarchy(type);
            DiagnosticInfo? diagnostic = null;

            if (!type.IsSealed && !isClosed)
            {
                diagnostic = new DiagnosticInfo(OpenHierarchyCase, type.Locations.FirstOrDefault(), type.Name);
            }
            else if (discriminator == null)
            {
                diagnostic = new DiagnosticInfo(MissingDiscriminator, type.Locations.FirstOrDefault(), type.Name);
            }

            return new HierarchyCaseModel(type, discriminator, diagnostic);
        }

        private static IEnumerable<INamedTypeSymbol> GetDirectCases(INamedTypeSymbol root)
        {
            foreach (var type in GetTypes(root.ContainingAssembly.GlobalNamespace))
            {
                if (type.BaseType != null &&
                    SymbolEqualityComparer.Default.Equals(type.BaseType.OriginalDefinition, root.OriginalDefinition))
                {
                    yield return type;
                }
            }
        }

        private static IEnumerable<INamedTypeSymbol> GetHierarchyCases(INamedTypeSymbol root)
        {
            foreach (var directCase in GetDirectCases(root))
            {
                if (!directCase.IsSealed && IsClosedHierarchy(directCase))
                {
                    foreach (var nestedCase in GetHierarchyCases(directCase))
                    {
                        yield return nestedCase;
                    }
                }
                else
                {
                    yield return directCase;
                }
            }
        }

        private static IEnumerable<INamedTypeSymbol> GetTypes(INamespaceSymbol space)
        {
            foreach (var member in space.GetMembers())
            {
                if (member is INamespaceSymbol childSpace)
                {
                    foreach (var type in GetTypes(childSpace)) yield return type;
                }
                else if (member is INamedTypeSymbol type)
                {
                    yield return type;
                    foreach (var nested in GetNestedTypes(type)) yield return nested;
                }
            }
        }

        private static IEnumerable<INamedTypeSymbol> GetNestedTypes(INamedTypeSymbol type)
        {
            foreach (var nested in type.GetTypeMembers())
            {
                yield return nested;
                foreach (var child in GetNestedTypes(nested)) yield return child;
            }
        }

        private static bool IsClosedHierarchy(INamedTypeSymbol type)
        {
            if (type.GetAttributes().Any(attribute =>
                attribute.AttributeClass?.ToDisplayString() == "System.Runtime.CompilerServices.ClosedAttribute" ||
                attribute.AttributeClass?.ToDisplayString() == "System.Runtime.CompilerServices.IsClosedTypeAttribute"))
            {
                return true;
            }

            return type.DeclaringSyntaxReferences.Any(reference =>
                reference.GetSyntax().DescendantTokens().Any(token => token.Text == "closed"));
        }

        private static string SelectConstructor(INamedTypeSymbol type, IReadOnlyList<MemberModel> members)
        {
            var constructors = type.InstanceConstructors
                .Where(ctor => ctor.DeclaredAccessibility == Accessibility.Public)
                .ToArray();
            var parameterless = constructors.FirstOrDefault(ctor => ctor.Parameters.Length == 0);
            if (parameterless != null)
            {
                return $"(mapper, _) => new {type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}()";
            }

            var marked = constructors.Where(ctor => HasAttribute(ctor.GetAttributes(), "LiteDB.BsonCtorAttribute")).ToArray();
            var candidates = marked.Length > 0 ? marked : constructors;
            var matching = candidates
                .Where(ctor => ctor.Parameters.All(parameter => members.Any(member =>
                    string.Equals(member.MemberName, parameter.Name, StringComparison.OrdinalIgnoreCase) &&
                    member.PropertyType == parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))))
                .ToArray();
            var selected = matching.Length == 1 ? matching[0] : matching.OrderBy(ctor => ctor.Parameters.Length).FirstOrDefault();
            if (selected == null)
            {
                return "null";
            }

            var arguments = selected.Parameters.Select(parameter =>
            {
                var member = members.First(item =>
                    string.Equals(item.MemberName, parameter.Name, StringComparison.OrdinalIgnoreCase) &&
                    item.PropertyType == parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                return $"mapper.Deserialize<{member.PropertyType}>(value[{Literal(member.FieldName)}])";
            });

            return $"(mapper, value) => new {type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}({string.Join(", ", arguments)})";
        }

        private static string EmitEntity(EntityModel model)
        {
            var namespaceStart = model.NamespaceName == null ? string.Empty : $"namespace {model.NamespaceName}\n{{\n";
            var namespaceEnd = model.NamespaceName == null ? string.Empty : "}\n";
            var indent = model.NamespaceName == null ? string.Empty : "    ";
            var className = model.TypeName + "_LiteDbMapper";

            return $@"// <auto-generated />
{namespaceStart}{indent}internal static class {className}
{indent}{{
{indent}    internal static global::LiteDB.EntityMapper Create()
{indent}    {{
{indent}        var mapper = new global::LiteDB.EntityMapper(typeof({model.FullyQualifiedTypeName}));
{indent}        mapper.CreateInstanceWithMapper = {model.ConstructorExpression};

{Indent(model.MemberInitializers, indent + "        ")}
{indent}        return mapper;
{indent}    }}
{indent}}}
{namespaceEnd}";
        }

        private static string EmitHierarchy(HierarchyModel hierarchy)
        {
            var namespaceName = hierarchy.RootSymbol.ContainingNamespace.IsGlobalNamespace
                ? null
                : hierarchy.RootSymbol.ContainingNamespace.ToDisplayString();
            var namespaceStart = namespaceName == null ? string.Empty : $"namespace {namespaceName}\n{{\n";
            var namespaceEnd = namespaceName == null ? string.Empty : "}\n";
            var indent = namespaceName == null ? string.Empty : "    ";
            var className = hierarchy.RootSymbol.Name + "_LiteDbHierarchy";
            var cases = hierarchy.Cases
                .Where(item => item.Discriminator != null && (item.TypeSymbol.IsSealed || item.IsClosed))
                .ToArray();

            var serializeCases = string.Join("\n", cases.Select(item =>
                $@"{indent}            case {item.TypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} item:
{indent}                var {item.TypeSymbol.Name.ToLowerInvariant()}Document = mapper.ToDocument(item);
{indent}                {item.TypeSymbol.Name.ToLowerInvariant()}Document[""kind""] = {Literal(item.Discriminator ?? string.Empty)};
{indent}                return {item.TypeSymbol.Name.ToLowerInvariant()}Document;"));
            var deserializeCases = string.Join("\n", cases.Select(item =>
                $@"{indent}                case {Literal(item.Discriminator ?? string.Empty)}:
{indent}                    return mapper.Deserialize<{item.TypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}>(document);"));

            return $@"// <auto-generated />
{namespaceStart}{indent}internal static class {className}
{indent}{{
{indent}    internal static global::LiteDB.BsonValue Serialize(global::LiteDB.BsonMapper mapper, object input)
{indent}    {{
{indent}        switch (input)
{indent}        {{
{serializeCases}
{indent}            default:
{indent}                throw new global::System.InvalidOperationException(""Unknown {hierarchy.RootSymbol.Name} case"");
{indent}        }}
{indent}    }}

{indent}    internal static object Deserialize(global::LiteDB.BsonMapper mapper, global::LiteDB.BsonValue value)
{indent}    {{
{indent}        var document = value.AsDocument;
{indent}        switch (document[""kind""].AsString)
{indent}        {{
{deserializeCases}
{indent}            default:
{indent}                throw new global::System.InvalidOperationException(""Unknown {hierarchy.RootSymbol.Name} discriminator"");
{indent}        }}
{indent}    }}
{indent}}}
{namespaceEnd}";
        }

        private static string EmitProvider(IReadOnlyList<EntityModel> models, IReadOnlyList<HierarchyModel> hierarchies)
        {
            var cases = string.Join("\n", models.Select(model =>
                $"            if (type == typeof({model.FullyQualifiedTypeName})) {{ mapper = {QualifiedMapperName(model)}.Create(); return true; }}"));
            var valueCases = string.Join("\n", hierarchies.Select(hierarchy =>
                $"            if (type == typeof({hierarchy.RootSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})) {{ serialize = {QualifiedHierarchyName(hierarchy)}.Serialize; deserialize = {QualifiedHierarchyName(hierarchy)}.Deserialize; return true; }}"));
            var dictionaryTypes = models
                .SelectMany(model => model.Members)
                .Select(member => member.TypeSymbol)
                .OfType<INamedTypeSymbol>()
                .Where(IsDictionaryType);
            valueCases += "\n" + string.Join("\n", DistinctNamedTypes(dictionaryTypes)
                .Select(type => EmitDictionaryValueCase(type)));
            var listTypes = models
                .SelectMany(model => model.Members)
                .Select(member => member.TypeSymbol)
                .OfType<INamedTypeSymbol>()
                .Where(IsListType);
            var setTypes = models
                .SelectMany(model => model.Members)
                .Select(member => member.TypeSymbol)
                .OfType<INamedTypeSymbol>()
                .Where(IsHashSetType);
            var collectionCases = string.Join("\n", DistinctNamedTypes(listTypes)
                .Select(type => EmitListProviderCase(type))
                .Concat(DistinctNamedTypes(setTypes)
                    .Select(type => EmitSetProviderCase(type))));

            return $@"// <auto-generated />
internal sealed class LiteDbGeneratedMapperProvider : global::LiteDB.AOT.IEntityMapperProvider, global::LiteDB.AOT.IGeneratedValueProvider, global::LiteDB.AOT.IGeneratedCollectionProvider
{{
    public bool TryGet(global::System.Type type, out global::LiteDB.EntityMapper mapper)
    {{
{cases}
         mapper = null;
         return false;
     }}

    public bool TryGet(global::System.Type type, out global::System.Func<global::LiteDB.BsonMapper, object, global::LiteDB.BsonValue> serialize, out global::System.Func<global::LiteDB.BsonMapper, global::LiteDB.BsonValue, object> deserialize)
    {{
{valueCases}
        serialize = null;
        deserialize = null;
        return false;
    }}

    public bool TryGet(global::System.Type type, out global::System.Func<global::LiteDB.BsonMapper, object, global::LiteDB.BsonArray> serialize, out global::System.Func<global::LiteDB.BsonMapper, global::LiteDB.BsonArray, object> deserialize)
    {{
{collectionCases}
        serialize = null;
        deserialize = null;
        return false;
    }}
}}
";
        }

        private static bool IsDictionaryType(INamedTypeSymbol type)
        {
            return type.Name == "Dictionary" &&
                type.ContainingNamespace.ToDisplayString() == "System.Collections.Generic" &&
                type.TypeArguments.Length == 2 &&
                type.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "string";
        }

        private static string EmitDictionaryValueCase(INamedTypeSymbol type)
        {
            var dictionaryType = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var valueType = type.TypeArguments[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            return $@"        if (type == typeof({dictionaryType}))
        {{
            serialize = (mapper, value) =>
            {{
                var source = (global::System.Collections.Generic.Dictionary<string, {valueType}>)value;
                var result = new global::LiteDB.BsonDocument();
                foreach (var pair in source)
                {{
                    result[pair.Key] = mapper.Serialize<{valueType}>(pair.Value);
                }}
                return result;
            }};
            deserialize = (mapper, value) =>
            {{
                var document = value.AsDocument;
                var result = new global::System.Collections.Generic.Dictionary<string, {valueType}>();
                foreach (var key in document.Keys)
                {{
                    result[key] = mapper.Deserialize<{valueType}>(document[key]);
                }}
                return result;
            }};
            return true;
        }}";
        }

        private static bool IsListType(INamedTypeSymbol type)
        {
            return type.Name == "List" &&
                type.ContainingNamespace.ToDisplayString() == "System.Collections.Generic" &&
                type.TypeArguments.Length == 1;
        }

        private static bool IsHashSetType(INamedTypeSymbol type)
        {
            return type.Name == "HashSet" &&
                type.ContainingNamespace.ToDisplayString() == "System.Collections.Generic" &&
                type.TypeArguments.Length == 1;
        }

        private static IEnumerable<INamedTypeSymbol> DistinctNamedTypes(IEnumerable<INamedTypeSymbol> types)
        {
            var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

            foreach (var type in types)
            {
                if (seen.Add(type)) yield return type;
            }
        }

        private static string EmitListProviderCase(INamedTypeSymbol type)
        {
            var collectionType = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var itemType = type.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            return $@"        if (type == typeof({collectionType}))
        {{
            serialize = (mapper, value) =>
            {{
                var result = new global::LiteDB.BsonArray();
                foreach (var item in (global::System.Collections.Generic.List<{itemType}>)value)
                {{
                    result.Add(mapper.Serialize<{itemType}>(item));
                }}
                return result;
            }};
            deserialize = (mapper, value) =>
            {{
                var result = new global::System.Collections.Generic.List<{itemType}>();
                foreach (var item in value)
                {{
                    result.Add(mapper.Deserialize<{itemType}>(item));
                }}
                return result;
            }};
            return true;
        }}";
        }

        private static string EmitSetProviderCase(INamedTypeSymbol type)
        {
            var collectionType = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var itemType = type.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            return $@"        if (type == typeof({collectionType}))
        {{
            serialize = (mapper, value) =>
            {{
                var result = new global::LiteDB.BsonArray();
                foreach (var item in (global::System.Collections.Generic.HashSet<{itemType}>)value)
                {{
                    result.Add(mapper.Serialize<{itemType}>(item));
                }}
                return result;
            }};
            deserialize = (mapper, value) =>
            {{
                var result = new global::System.Collections.Generic.HashSet<{itemType}>();
                foreach (var item in value)
                {{
                    result.Add(mapper.Deserialize<{itemType}>(item));
                }}
                return result;
            }};
            return true;
        }}";
        }

        private static string QualifiedHierarchyName(HierarchyModel hierarchy)
        {
            var name = hierarchy.RootSymbol.Name + "_LiteDbHierarchy";
            return hierarchy.RootSymbol.ContainingNamespace.IsGlobalNamespace
                ? name
                : hierarchy.RootSymbol.ContainingNamespace.ToDisplayString() + "." + name;
        }

        private static string QualifiedMapperName(EntityModel model)
        {
            return model.NamespaceName == null
                ? model.TypeName + "_LiteDbMapper"
                : model.NamespaceName + "." + model.TypeName + "_LiteDbMapper";
        }

        private static string Indent(string value, string indent)
        {
            return string.Join("\n", value.Split(new[] { '\n' }, StringSplitOptions.None)
                .Select(line => line.Length == 0 ? line : indent + line));
        }

        private static bool HasAttribute(IEnumerable<AttributeData> attributes, string metadataName)
        {
            return attributes.Any(attribute => attribute.AttributeClass?.ToDisplayString() == metadataName);
        }

        private static bool GetAutoId(IEnumerable<AttributeData> attributes)
        {
            var attribute = attributes.FirstOrDefault(item => item.AttributeClass?.ToDisplayString() == IdAttributeName);
            return attribute == null || attribute.ConstructorArguments.Length == 0 ||
                attribute.ConstructorArguments[0].Value is not bool value || value;
        }

        private static string? GetFieldName(IEnumerable<AttributeData> attributes)
        {
            var attribute = attributes.FirstOrDefault(item => item.AttributeClass?.ToDisplayString() == FieldAttributeName);
            return attribute?.ConstructorArguments.Length == 1 &&
                attribute.ConstructorArguments[0].Value is string name ? name : null;
        }

        private static string Literal(string value)
        {
            return SymbolDisplay.FormatLiteral(value, quote: true);
        }

        private readonly struct MemberModel
        {
            public MemberModel(string memberName, string propertyType, string fieldName, ITypeSymbol typeSymbol, string initializer)
            {
                MemberName = memberName;
                PropertyType = propertyType;
                FieldName = fieldName;
                TypeSymbol = typeSymbol;
                Initializer = initializer;
            }

            public string MemberName { get; }
            public string PropertyType { get; }
            public string FieldName { get; }
            public ITypeSymbol TypeSymbol { get; }
            public string Initializer { get; }
        }

        private readonly struct DiagnosticInfo
        {
            public DiagnosticInfo(DiagnosticDescriptor descriptor, Location? location, params object[] arguments)
            {
                Descriptor = descriptor;
                Location = location;
                Arguments = arguments;
            }

            public DiagnosticDescriptor Descriptor { get; }
            public Location? Location { get; }
            public object[] Arguments { get; }
        }

        private readonly struct HierarchyCaseModel
        {
            public HierarchyCaseModel(INamedTypeSymbol typeSymbol, string? discriminator, DiagnosticInfo? diagnostic)
            {
                TypeSymbol = typeSymbol;
                Discriminator = discriminator;
                Diagnostic = diagnostic;
                IsClosed = IsClosedHierarchy(typeSymbol);
            }

            public INamedTypeSymbol TypeSymbol { get; }
            public string? Discriminator { get; }
            public DiagnosticInfo? Diagnostic { get; }
            public bool IsClosed { get; }
        }

        private readonly struct HierarchyModel
        {
            public HierarchyModel(INamedTypeSymbol rootSymbol, IReadOnlyList<HierarchyCaseModel> cases, IReadOnlyList<DiagnosticInfo> diagnostics)
            {
                RootSymbol = rootSymbol;
                Cases = cases;
                Diagnostics = diagnostics;
                HintName = rootSymbol.ToDisplayString()
                    .Replace("global::", string.Empty)
                    .Replace('<', '_')
                    .Replace('>', '_')
                    .Replace('.', '_') + ".LiteDbHierarchy.g.cs";
            }

            public INamedTypeSymbol RootSymbol { get; }
            public IReadOnlyList<HierarchyCaseModel> Cases { get; }
            public IReadOnlyList<DiagnosticInfo> Diagnostics { get; }
            public string HintName { get; }
        }

        private readonly record struct EntityModel
        {
            public EntityModel(string? namespaceName, string typeName, string fullyQualifiedTypeName, string hintName, string memberInitializers, string constructorExpression, INamedTypeSymbol typeSymbol, IReadOnlyList<MemberModel> members)
            {
                NamespaceName = namespaceName;
                TypeName = typeName;
                FullyQualifiedTypeName = fullyQualifiedTypeName;
                HintName = hintName;
                MemberInitializers = memberInitializers;
                ConstructorExpression = constructorExpression;
                TypeSymbol = typeSymbol;
                Members = members;
            }

            public string? NamespaceName { get; }
            public string TypeName { get; }
            public string FullyQualifiedTypeName { get; }
            public string HintName { get; }
            public string MemberInitializers { get; }
            public string ConstructorExpression { get; }
            public INamedTypeSymbol TypeSymbol { get; }
            public IReadOnlyList<MemberModel> Members { get; }
        }
    }
}
