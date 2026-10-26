using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CFITS.Generator;

[Generator]
public sealed partial class FitsContextGenerator : IIncrementalGenerator
{
    private const string NewLine = "\n";
    // Bulk copies amortize their buffers and setup only on larger tables.
    private const int BulkReadRowThreshold = 8_192;

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var contexts = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => node is ClassDeclarationSyntax declaration && declaration.AttributeLists.Count > 0,
            static (syntax, _) => Describe(syntax)).Where(static model => model is not null);
        context.RegisterSourceOutput(contexts, static (output, model) => Emit(output, model!));
    }

    private static ContextModel? Describe(GeneratorSyntaxContext syntax)
    {
        if (syntax.SemanticModel.GetDeclaredSymbol(syntax.Node) is not INamedTypeSymbol symbol)
        {
            return null;
        }

        if (!InheritsContext(symbol))
        {
            return null;
        }

        var models = new List<TypeModel>();
        var documents = new List<DocumentModel>();
        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass?.Name != "FitsSerializableAttribute")
            {
                continue;
            }

            if (attribute.ConstructorArguments.FirstOrDefault().Value is not INamedTypeSymbol target)
            {
                continue;
            }

            if (target.GetAttributes().Any(static item => item.AttributeClass?.Name == "FitsDocumentModelAttribute"))
            {
                documents.Add(DescribeDocument(target));
            }
            else
            {
                models.Add(DescribeType(target));
            }
        }

        return models.Count == 0 && documents.Count == 0 ? null : new ContextModel(symbol, models, documents);
    }

    private static bool InheritsContext(INamedTypeSymbol symbol)
    {
        for (var current = symbol.BaseType; current is not null; current = current.BaseType)
        {
            if (current.Name == "FitsSerializerContext")
            {
                return true;
            }
        }

        return false;
    }

    private static TypeModel DescribeType(INamedTypeSymbol target)
    {
        bool table = false;
        string? hduName = null;
        int hduIndex = -1;
        foreach (var attribute in target.GetAttributes())
        {
            if (attribute.AttributeClass?.Name == "FitsTableAttribute")
            {
                table = true;
                hduName = attribute.ConstructorArguments.FirstOrDefault().Value as string;
                hduIndex = GetHduIndex(attribute);
            }

            if (attribute.AttributeClass?.Name == "FitsImageAttribute")
            {
                hduName = attribute.ConstructorArguments.FirstOrDefault().Value as string;
                hduIndex = GetHduIndex(attribute);
            }
        }

        var members = new List<MemberModel>();
        foreach (var member in target.GetMembers().OfType<IPropertySymbol>())
        {
            foreach (var attribute in member.GetAttributes())
            {
                string? name = attribute.ConstructorArguments.FirstOrDefault().Value as string;
                if (attribute.AttributeClass?.Name == "FitsKeywordAttribute" && name is not null)
                {
                    members.Add(new MemberModel(member, MemberRole.Keyword, name));
                }
                else if (attribute.AttributeClass?.Name == "FitsColumnAttribute" && name is not null)
                {
                    members.Add(new MemberModel(member, MemberRole.Column, name));
                }
                else if (attribute.AttributeClass?.Name == "FitsDataAttribute")
                {
                    members.Add(new MemberModel(member, MemberRole.Data, member.Name));
                }
            }
        }

        return new TypeModel(target, table, hduName, hduIndex, members);
    }

    private static DocumentModel DescribeDocument(INamedTypeSymbol target)
    {
        var members = new List<DocumentMemberModel>();
        foreach (var property in target.GetMembers().OfType<IPropertySymbol>())
        {
            AttributeData? image = property.GetAttributes().FirstOrDefault(
                static item => item.AttributeClass?.Name == "FitsImageAttribute");
            AttributeData? table = property.GetAttributes().FirstOrDefault(
                static item => item.AttributeClass?.Name == "FitsTableAttribute");
            bool unknown = property.GetAttributes().Any(
                static item => item.AttributeClass?.Name == "FitsUnknownHdusAttribute");
            bool special = property.GetAttributes().Any(
                static item => item.AttributeClass?.Name == "FitsSpecialRecordsAttribute");
            if (image is not null)
            {
                members.Add(new DocumentMemberModel(
                    property,
                    DescribeDocumentChild(target.Name, property, image, table: false),
                    DocumentMemberRole.Image));
            }
            else if (table is not null)
            {
                members.Add(new DocumentMemberModel(
                    property,
                    DescribeDocumentChild(target.Name, property, table, table: true),
                    DocumentMemberRole.Table));
            }
            else if (unknown)
            {
                members.Add(new DocumentMemberModel(property, null, DocumentMemberRole.Unknown));
            }
            else if (special)
            {
                members.Add(new DocumentMemberModel(property, null, DocumentMemberRole.SpecialRecords));
            }
        }

        return new DocumentModel(target, members);
    }

    private static TypeModel DescribeDocumentChild(
        string documentName,
        IPropertySymbol property,
        AttributeData attribute,
        bool table)
    {
        ITypeSymbol child = property.Type;
        if (table && child is INamedTypeSymbol list
            && list.TypeArguments.Length == 1)
        {
            child = list.TypeArguments[0];
        }

        if (child is not INamedTypeSymbol named)
        {
            throw new InvalidOperationException(
                $"The document property '{property.Name}' must use a named generated type.");
        }

        string? name = attribute.NamedArguments.FirstOrDefault(
            static item => item.Key == "Name").Value.Value as string;
        name ??= attribute.ConstructorArguments.FirstOrDefault().Value as string;
        if (table)
        {
            name ??= DescribeType(named).HduName;
        }
        return new TypeModel(
            named,
            table,
            name,
            GetHduIndex(attribute),
            DescribeType(named).Members,
            $"{named.Name}_{property.Name}_{documentName}Info");
    }

    private static int GetHduIndex(AttributeData attribute)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == "HduIndex" && argument.Value.Value is int index)
            {
                return index;
            }
        }

        return -1;
    }

    private static void Emit(SourceProductionContext output, ContextModel model)
    {
        string space = model.Symbol.ContainingNamespace.IsGlobalNamespace
            ? ""
            : model.Symbol.ContainingNamespace.ToDisplayString();

        string namespaceDeclaration = space.Length == 0
            ? ""
            : $"namespace {space};{NewLine}{NewLine}";
        string properties = string.Join(NewLine, model.Types.Select(static type =>
        {
            string property = type.Symbol.Name;
            string info = type.Table ? "CFITS.FitsTableTypeInfo" : "CFITS.FitsTypeInfo";
            return $"    private static readonly {info}<{type.Symbol.ToDisplayString()}> __{property} = new {property}Info();{NewLine}    public {info}<{type.Symbol.ToDisplayString()}> {property} => __{property};";
        }));
        string documentProperties = string.Join(NewLine, model.Documents.Select(document =>
            $"    private static readonly CFITS.FitsDocumentTypeInfo<{document.Symbol.ToDisplayString()}> __{document.Symbol.Name} = new {document.Symbol.Name}Info();{NewLine}    public CFITS.FitsDocumentTypeInfo<{document.Symbol.ToDisplayString()}> {document.Symbol.Name} => __{document.Symbol.Name};"));
        string types = string.Join(NewLine, model.Types.Select(static type => type.Table ? EmitTable(type) : EmitImage(type)));
        string documents = string.Join(NewLine, model.Documents.Select(document => EmitDocument(document, model.Types)));
        string code = $$"""
            #nullable enable
            {{namespaceDeclaration}}public partial class {{model.Symbol.Name}}
            {
                public static {{model.Symbol.Name}} Default { get; } = new();
            {{properties}}
            {{documentProperties}}
            {{types}}
            {{documents}}
            }
            """;

        output.AddSource($"{model.Symbol.Name}.g.cs", SourceText.From(code, Encoding.UTF8));
    }
}
