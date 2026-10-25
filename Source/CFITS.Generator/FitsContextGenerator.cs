using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CFITS.Generator;

[Generator]
public sealed class FitsContextGenerator : IIncrementalGenerator
{
    private const string NewLine = "\n";

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

            models.Add(DescribeType(target));
        }

        return models.Count == 0 ? null : new ContextModel(symbol, models);
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
        foreach (var attribute in target.GetAttributes())
        {
            if (attribute.AttributeClass?.Name == "FitsTableAttribute")
            {
                table = true;
                hduName = attribute.ConstructorArguments.FirstOrDefault().Value as string;
            }

            if (attribute.AttributeClass?.Name == "FitsImageAttribute")
            {
                hduName = attribute.ConstructorArguments.FirstOrDefault().Value as string;
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

        return new TypeModel(target, table, hduName, members);
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
            return $"    public {info}<{type.Symbol.ToDisplayString()}> {property} {{ get; }} = new {property}Info();";
        }));
        string types = string.Join(NewLine, model.Types.Select(static type => type.Table ? EmitTable(type) : EmitImage(type)));
        string code = $$"""
            {{namespaceDeclaration}}public partial class {{model.Symbol.Name}}
            {
                public static {{model.Symbol.Name}} Default { get; } = new();
            {{properties}}
            {{types}}
            }
            """;

        output.AddSource($"{model.Symbol.Name}.g.cs", SourceText.From(code, Encoding.UTF8));
    }

    private static string EmitImage(TypeModel type)
    {
        string name = type.Symbol.ToDisplayString();
        var data = type.Members.FirstOrDefault(static member => member.Role == MemberRole.Data);
        string keywords = string.Join(NewLine, type.Members
            .Where(static member => member.Role == MemberRole.Keyword)
            .Select(static member => $"                {member.Symbol.Name} = {ReadKeyword(member)},"));
        string dataAssignment = "";
        if (data is not null && data.Symbol.Type is IArrayTypeSymbol array)
        {
            string value = array.Rank > 3
                ? "throw new System.NotSupportedException(\"Generated image serialization supports arrays up to rank 3.\")"
                : ReadArray(array);
            dataAssignment = $"                {data.Symbol.Name} = {value},";
        }

        string write = data is not null && data.Symbol.Type is IArrayTypeSymbol written
            ? EmitImageWrite(type, written, data.Symbol.Name)
            : $$"""
                        var built = CFITS.FitsDocumentBuilder.Create().Image(System.ReadOnlySpan<int>.Empty, System.ReadOnlySpan<byte>.Empty, header =>
                        {
            {{EmitKeywordWrites(type, "value")}}
                        }).Build();
                        built.Write(stream);
            """;
        return $$"""
                private sealed class {{type.Symbol.Name}}Info : CFITS.FitsTypeInfo<{{name}}>
                {
                    public override {{name}} Read(System.IO.Stream stream)
                    {
                        using var document = CFITS.FitsDocument.Open(stream);
                        var hdu = {{FindImage(type.HduName)}};
                        var image = hdu.Image ?? throw new CFITS.FitsException("The HDU is not an image.");
                        return new {{name}}
                        {
            {{keywords}}{{(keywords.Length > 0 && dataAssignment.Length > 0 ? NewLine : "")}}{{dataAssignment}}
                        };
                    }
                    public override void Write(System.IO.Stream stream, {{name}} value)
                    {
            {{write}}
                    }
                }
            """;
    }

    private static string EmitImageWrite(TypeModel type, IArrayTypeSymbol array, string property)
    {
        if (array.Rank > 3)
        {
            return "            throw new System.NotSupportedException(\"Generated image serialization supports arrays up to rank 3.\");";
        }

        string element = ElementName(array.ElementType);
        string dimensions;
        if (array.Rank == 1)
        {
            dimensions = $$"""
                        int n1 = pixels.Length;
                        var built = CFITS.FitsDocumentBuilder.Create().Image(new int[] { n1 }, pixels, header =>
                """;
        }
        else if (array.Rank == 2)
        {
            dimensions = $$"""
                        int n2 = pixels.GetLength(0);
                        int n1 = pixels.GetLength(1);
                        var stored = new {{element}}[n1 * n2];
                        for (int y = 0; y < n2; y++) for (int x = 0; x < n1; x++) stored[x + (y * n1)] = pixels[y, x];
                        var built = CFITS.FitsDocumentBuilder.Create().Image(new int[] { n1, n2 }, stored, header =>
                """;
        }
        else
        {
            dimensions = $$"""
                        int n3 = pixels.GetLength(0);
                        int n2 = pixels.GetLength(1);
                        int n1 = pixels.GetLength(2);
                        var stored = new {{element}}[n1 * n2 * n3];
                        for (int z = 0; z < n3; z++) for (int y = 0; y < n2; y++) for (int x = 0; x < n1; x++) stored[x + n1 * (y + (n2 * z))] = pixels[z, y, x];
                        var built = CFITS.FitsDocumentBuilder.Create().Image(new int[] { n1, n2, n3 }, stored, header =>
                """;
        }

        return $$"""
                    var pixels = value.{{property}} ?? throw new CFITS.FitsException("Image data is missing.");
            {{dimensions}}
                    {
            {{EmitKeywordWrites(type, "value")}}
                    }).Build();
                    built.Write(stream);
            """;
    }

    private static string EmitTable(TypeModel type)
    {
        string name = type.Symbol.ToDisplayString();
        string columns = string.Join(NewLine, type.Members
            .Where(static member => member.Role == MemberRole.Column)
            .Select(static member => $"                    {member.Symbol.Name} = {ReadColumn(member)},"));
        string writes = string.Join(NewLine, type.Members
            .Where(static member => member.Role == MemberRole.Column)
            .Select(EmitColumnWrite));
        string extensionName = string.IsNullOrEmpty(type.HduName)
            ? ""
            : $"                header.Text(\"EXTNAME\", {Literal(type.HduName!)});";
        return $$"""
                private sealed class {{type.Symbol.Name}}Info : CFITS.FitsTableTypeInfo<{{name}}>
                {
                    public override System.Collections.Generic.IReadOnlyList<{{name}}> Read(System.IO.Stream stream)
                    {
                        using var document = CFITS.FitsDocument.Open(stream);
                        var hdu = {{FindTable(type.HduName)}};
                        var table = hdu.Table ?? throw new CFITS.FitsException("The HDU is not a table.");
                        var rows = new System.Collections.Generic.List<{{name}}>(table.RowCount);
                        for (int row = 0; row < table.RowCount; row++)
                        {
                            rows.Add(new {{name}}
                            {
            {{columns}}
                            });
                        }
                        return rows;
                    }
                    public override void Write(System.IO.Stream stream, System.Collections.Generic.IReadOnlyList<{{name}}> rows)
                    {
                        var table = new CFITS.BinaryTableBuilder();
            {{writes}}
                        var built = CFITS.FitsDocumentBuilder.Create().BinaryTable(table, header =>
                        {
            {{extensionName}}
                        }).Build();
                        built.Write(stream);
                    }
                }
            """;
    }

    private static string EmitColumnWrite(MemberModel member)
    {
        string local = member.Symbol.Name.ToLowerInvariant() + "Values";
        string element = ElementName(member.Symbol.Type);
        if (member.Symbol.Type.SpecialType == SpecialType.System_String)
        {
            return $$"""
                        var {{local}} = new {{element}}[rows.Count];
                        for (int index = 0; index < rows.Count; index++) {{local}}[index] = rows[index].{{member.Symbol.Name}};
                        int {{local}}Width = 1;
                        foreach (var item in {{local}}) if (item is not null && item.Length > {{local}}Width) {{local}}Width = item.Length;
                        table.Add({{Literal(member.ColumnName)}}, {{local}}, {{local}}Width);
                """;
        }
        return $$"""
                    var {{local}} = new {{element}}[rows.Count];
                    for (int index = 0; index < rows.Count; index++) {{local}}[index] = rows[index].{{member.Symbol.Name}};
                    table.Add({{Literal(member.ColumnName)}}, {{local}});
            """;
    }

    private static string EmitKeywordWrites(TypeModel type, string variable)
    {
        return string.Join(NewLine, type.Members
            .Where(static item => item.Role == MemberRole.Keyword)
            .Select(member => $"                {WriteKeyword(member, variable)};"));
    }

    private static string WriteKeyword(MemberModel member, string variable)
    {
        string value = variable + "." + member.Symbol.Name;
        return member.Symbol.Type.SpecialType switch
        {
            SpecialType.System_Boolean => $"header.Logical({Literal(member.ColumnName)}, {value})",
            SpecialType.System_Int32 or SpecialType.System_Int16 => $"header.Integer({Literal(member.ColumnName)}, {value})",
            SpecialType.System_Int64 => $"header.Integer({Literal(member.ColumnName)}, {value})",
            SpecialType.System_Double or SpecialType.System_Single => $"header.Real({Literal(member.ColumnName)}, {value})",
            SpecialType.System_String => $"header.Text({Literal(member.ColumnName)}, {value} ?? \"\")",
            _ => $"header.Text({Literal(member.ColumnName)}, {value}?.ToString() ?? \"\")",
        };
    }

    private static string ReadKeyword(MemberModel member)
    {
        string keyword = member.ColumnName;
        return member.Symbol.Type.SpecialType switch
        {
            SpecialType.System_Boolean => $"hdu.Header.GetBoolean({Literal(keyword)})",
            SpecialType.System_Int32 => $"(int)hdu.Header.GetInt64({Literal(keyword)})",
            SpecialType.System_Int16 => $"(short)hdu.Header.GetInt64({Literal(keyword)})",
            SpecialType.System_Int64 => $"hdu.Header.GetInt64({Literal(keyword)})",
            SpecialType.System_Double => $"hdu.Header.GetDouble({Literal(keyword)})",
            SpecialType.System_Single => $"(float)hdu.Header.GetDouble({Literal(keyword)})",
            SpecialType.System_String => $"hdu.Header.GetString({Literal(keyword)})",
            _ => $"hdu.Header.GetString({Literal(keyword)})",
        };
    }

    private static string ReadColumn(MemberModel member)
    {
        string column = $"table.Column({Literal(member.ColumnName)})";
        return member.Symbol.Type.SpecialType switch
        {
            SpecialType.System_Boolean => $"{column}.ReadLogical(row)",
            SpecialType.System_Int32 => $"(int){column}.ReadInteger(row)",
            SpecialType.System_Int16 => $"(short){column}.ReadInteger(row)",
            SpecialType.System_Int64 => $"{column}.ReadInteger(row)",
            SpecialType.System_Byte => $"(byte){column}.ReadInteger(row)",
            SpecialType.System_Double => $"{column}.ReadReal(row)",
            SpecialType.System_Single => $"(float){column}.ReadReal(row)",
            SpecialType.System_String => $"{column}.ReadString(row)",
            _ => $"{column}.ReadString(row)",
        };
    }

    private static string ReadArray(IArrayTypeSymbol array)
    {
        string element = ElementName(array.ElementType);
        string cast = array.ElementType.SpecialType == SpecialType.System_Double ? "" : $"({element})";
        if (array.Rank == 1)
        {
            return $"ReadRank1{element}(image)".Replace("ReadRank1" + element, $"new Func<{element}[]>(() => {{ var values = new {element}[image.Length]; for (int i = 0; i < image.Length; i++) values[i] = {cast}image.ReadPhysical(i); return values; }})()");
        }

        if (array.Rank == 2)
        {
            return $"new Func<{element}[,]>(() => {{ int n1 = image.Axes[0]; int n2 = image.Axes.Count > 1 ? image.Axes[1] : 1; var values = new {element}[n2, n1]; for (int y = 0; y < n2; y++) for (int x = 0; x < n1; x++) values[y, x] = {cast}image.ReadPhysical(x + (y * n1)); return values; }})()";
        }

        return $"new Func<{element}[,,]>(() => {{ int n1 = image.Axes[0]; int n2 = image.Axes[1]; int n3 = image.Axes[2]; var values = new {element}[n3, n2, n1]; for (int z = 0; z < n3; z++) for (int y = 0; y < n2; y++) for (int x = 0; x < n1; x++) values[z, y, x] = {cast}image.ReadPhysical(x + n1 * (y + (n2 * z))); return values; }})()";
    }

    private static string FindImage(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "System.Linq.Enumerable.First(document.Hdus, hdu => hdu.Kind == CFITS.FitsHduKind.Image)";
        }

        return $"System.Linq.Enumerable.First(document.Hdus, hdu => hdu.Kind == CFITS.FitsHduKind.Image && hdu.Header.TryGetCard(\"EXTNAME\", out var extname) && extname.GetString() == {Literal(name ?? "")})";
    }

    private static string FindTable(string? name)
    {
        string kinds = "hdu.Kind == CFITS.FitsHduKind.BinaryTable || hdu.Kind == CFITS.FitsHduKind.AsciiTable";
        if (string.IsNullOrEmpty(name))
        {
            return $"System.Linq.Enumerable.First(document.Hdus, hdu => {kinds})";
        }

        return $"System.Linq.Enumerable.First(document.Hdus, hdu => ({kinds}) && hdu.Header.TryGetCard(\"EXTNAME\", out var extname) && extname.GetString() == {Literal(name ?? "")})";
    }

    private static string ElementName(ITypeSymbol type) => type.SpecialType switch
    {
        SpecialType.System_Byte => "byte",
        SpecialType.System_Int16 => "short",
        SpecialType.System_Int32 => "int",
        SpecialType.System_Int64 => "long",
        SpecialType.System_Single => "float",
        SpecialType.System_Double => "double",
        SpecialType.System_Boolean => "bool",
        SpecialType.System_String => "string",
        _ => type.ToDisplayString(),
    };

    private static string Literal(string value) => SymbolDisplay.FormatLiteral(value, quote: true);

    private sealed class ContextModel
    {
        public ContextModel(INamedTypeSymbol symbol, List<TypeModel> types)
        {
            Symbol = symbol;
            Types = types;
        }

        public INamedTypeSymbol Symbol { get; }

        public List<TypeModel> Types { get; }
    }

    private sealed class TypeModel
    {
        public TypeModel(INamedTypeSymbol symbol, bool table, string? hduName, List<MemberModel> members)
        {
            Symbol = symbol;
            Table = table;
            HduName = hduName;
            Members = members;
        }

        public INamedTypeSymbol Symbol { get; }

        public bool Table { get; }

        public string? HduName { get; }

        public List<MemberModel> Members { get; }
    }

    private sealed class MemberModel
    {
        public MemberModel(IPropertySymbol symbol, MemberRole role, string columnName)
        {
            Symbol = symbol;
            Role = role;
            ColumnName = columnName;
        }

        public IPropertySymbol Symbol { get; }

        public MemberRole Role { get; }

        public string ColumnName { get; }
    }

    private enum MemberRole
    {
        Keyword,
        Column,
        Data,
    }
}
