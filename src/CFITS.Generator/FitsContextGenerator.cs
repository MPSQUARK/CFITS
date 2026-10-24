using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CFITS.Generator;

[Generator]
public sealed class FitsContextGenerator : IIncrementalGenerator
{
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
        var code = new StringBuilder();
        string space = model.Symbol.ContainingNamespace.IsGlobalNamespace
            ? ""
            : model.Symbol.ContainingNamespace.ToDisplayString();
        if (space.Length > 0)
        {
            code.Append("namespace ").Append(space).AppendLine(";");
            code.AppendLine();
        }

        code.Append("public partial class ").Append(model.Symbol.Name).AppendLine();
        code.AppendLine("{");
        code.Append("    public static ").Append(model.Symbol.Name).Append(" Default { get; } = new();").AppendLine();
        foreach (var type in model.Types)
        {
            string property = type.Symbol.Name;
            string info = type.Table ? "CFITS.FitsTableTypeInfo" : "CFITS.FitsTypeInfo";
            code.Append("    public ").Append(info).Append('<').Append(type.Symbol.ToDisplayString()).Append("> ").Append(property)
                .Append(" { get; } = new ").Append(property).AppendLine("Info();");
        }

        foreach (var type in model.Types)
        {
            if (type.Table)
            {
                EmitTable(code, type);
            }
            else
            {
                EmitImage(code, type);
            }
        }

        code.AppendLine("}");
        output.AddSource($"{model.Symbol.Name}.g.cs", SourceText.From(code.ToString(), Encoding.UTF8));
    }

    private static void EmitImage(StringBuilder code, TypeModel type)
    {
        string name = type.Symbol.ToDisplayString();
        code.Append("    private sealed class ").Append(type.Symbol.Name).Append("Info : CFITS.FitsTypeInfo<").Append(name).AppendLine(">");
        code.AppendLine("    {");
        code.Append("        public override ").Append(name).AppendLine(" Read(System.IO.Stream stream)");
        code.AppendLine("        {");
        code.AppendLine("            using var document = CFITS.FitsDocument.Open(stream);");
        code.Append("            var hdu = ").Append(FindImage(type.HduName)).AppendLine(";");
        code.AppendLine("            var image = hdu.Image ?? throw new CFITS.FitsException(\"The HDU is not an image.\");");
        var data = type.Members.FirstOrDefault(static member => member.Role == MemberRole.Data);
        code.Append("            return new ").Append(name).AppendLine();
        code.AppendLine("            {");
        foreach (var member in type.Members.Where(static item => item.Role == MemberRole.Keyword))
        {
            code.Append("                ").Append(member.Symbol.Name).Append(" = ").Append(ReadKeyword(member)).AppendLine(",");
        }

        if (data is not null && data.Symbol.Type is IArrayTypeSymbol array)
        {
            code.Append("                ").Append(data.Symbol.Name).Append(" = ").Append(ReadArray(array)).AppendLine(",");
        }

        code.AppendLine("            };");
        code.AppendLine("        }");
        code.Append("        public override void Write(System.IO.Stream stream, ").Append(name).AppendLine(" value)");
        code.AppendLine("        {");
        if (data is not null && data.Symbol.Type is IArrayTypeSymbol written)
        {
            EmitImageWrite(code, type, written, data.Symbol.Name);
        }
        else
        {
            code.AppendLine("            var built = CFITS.FitsDocumentBuilder.Create().Image(System.ReadOnlySpan<int>.Empty, System.ReadOnlySpan<byte>.Empty, header =>");
            code.AppendLine("            {");
            EmitKeywordWrites(code, type, "value");
            code.AppendLine("            }).Build();");
            code.AppendLine("            built.Write(stream);");
        }

        code.AppendLine("        }");
        code.AppendLine("    }");
    }

    private static void EmitImageWrite(StringBuilder code, TypeModel type, IArrayTypeSymbol array, string property)
    {
        string element = ElementName(array.ElementType);
        code.AppendLine("            var pixels = value." + property + " ?? throw new CFITS.FitsException(\"Image data is missing.\");");
        if (array.Rank == 1)
        {
            code.AppendLine("            int n1 = pixels.Length;");
            code.Append("            var stored = new ").Append(element).AppendLine("[n1];");
            code.AppendLine("            for (int x = 0; x < n1; x++) stored[x] = pixels[x];");
            code.Append("            var built = CFITS.FitsDocumentBuilder.Create().Image(new int[] { n1 }, stored, header =>");
        }
        else if (array.Rank == 2)
        {
            code.AppendLine("            int n2 = pixels.GetLength(0);");
            code.AppendLine("            int n1 = pixels.GetLength(1);");
            code.Append("            var stored = new ").Append(element).AppendLine("[n1 * n2];");
            code.AppendLine("            for (int y = 0; y < n2; y++) for (int x = 0; x < n1; x++) stored[x + (y * n1)] = pixels[y, x];");
            code.Append("            var built = CFITS.FitsDocumentBuilder.Create().Image(new int[] { n1, n2 }, stored, header =>");
        }
        else
        {
            code.AppendLine("            int n3 = pixels.GetLength(0);");
            code.AppendLine("            int n2 = pixels.GetLength(1);");
            code.AppendLine("            int n1 = pixels.GetLength(2);");
            code.Append("            var stored = new ").Append(element).AppendLine("[n1 * n2 * n3];");
            code.AppendLine("            for (int z = 0; z < n3; z++) for (int y = 0; y < n2; y++) for (int x = 0; x < n1; x++) stored[x + n1 * (y + (n2 * z))] = pixels[z, y, x];");
            code.Append("            var built = CFITS.FitsDocumentBuilder.Create().Image(new int[] { n1, n2, n3 }, stored, header =>");
        }

        code.AppendLine();
        code.AppendLine("            {");
        EmitKeywordWrites(code, type, "value");
        code.AppendLine("            }).Build();");
        code.AppendLine("            built.Write(stream);");
    }

    private static void EmitTable(StringBuilder code, TypeModel type)
    {
        string name = type.Symbol.ToDisplayString();
        code.Append("    private sealed class ").Append(type.Symbol.Name).Append("Info : CFITS.FitsTableTypeInfo<").Append(name).AppendLine(">");
        code.AppendLine("    {");
        code.Append("        public override System.Collections.Generic.IReadOnlyList<").Append(name).AppendLine("> Read(System.IO.Stream stream)");
        code.AppendLine("        {");
        code.AppendLine("            using var document = CFITS.FitsDocument.Open(stream);");
        code.Append("            var hdu = ").Append(FindTable(type.HduName)).AppendLine(";");
        code.AppendLine("            var table = hdu.Table ?? throw new CFITS.FitsException(\"The HDU is not a table.\");");
        code.Append("            var rows = new System.Collections.Generic.List<").Append(name).AppendLine(">(table.RowCount);");
        code.AppendLine("            for (int row = 0; row < table.RowCount; row++)");
        code.AppendLine("            {");
        code.Append("                rows.Add(new ").Append(name).AppendLine();
        code.AppendLine("                {");
        foreach (var member in type.Members.Where(static item => item.Role == MemberRole.Column))
        {
            code.Append("                    ").Append(member.Symbol.Name).Append(" = ").Append(ReadColumn(member)).AppendLine(",");
        }

        code.AppendLine("                });");
        code.AppendLine("            }");
        code.AppendLine("            return rows;");
        code.AppendLine("        }");
        code.Append("        public override void Write(System.IO.Stream stream, System.Collections.Generic.IReadOnlyList<").Append(name).AppendLine("> rows)");
        code.AppendLine("        {");
        code.AppendLine("            var table = new CFITS.BinaryTableBuilder();");
        foreach (var member in type.Members.Where(static item => item.Role == MemberRole.Column))
        {
            EmitColumnWrite(code, member);
        }

        code.AppendLine("            var built = CFITS.FitsDocumentBuilder.Create().BinaryTable(table, header =>");
        code.AppendLine("            {");
        if (!string.IsNullOrEmpty(type.HduName))
        {
            code.Append("                header.Text(\"EXTNAME\", \"").Append(type.HduName).AppendLine("\");");
        }

        code.AppendLine("            }).Build();");
        code.AppendLine("            built.Write(stream);");
        code.AppendLine("        }");
        code.AppendLine("    }");
    }

    private static void EmitColumnWrite(StringBuilder code, MemberModel member)
    {
        string local = member.Symbol.Name.ToLowerInvariant() + "Values";
        string element = ElementName(member.Symbol.Type);
        code.Append("            var ").Append(local).Append(" = new ").Append(element).AppendLine("[rows.Count];");
        code.Append("            for (int index = 0; index < rows.Count; index++) ").Append(local).Append("[index] = rows[index].").Append(member.Symbol.Name).AppendLine(";");
        if (member.Symbol.Type.SpecialType == SpecialType.System_String)
        {
            code.Append("            int ").Append(local).AppendLine("Width = 1;");
            code.Append("            foreach (var item in ").Append(local).Append(") if (item is not null && item.Length > ").Append(local).Append("Width) ").Append(local).AppendLine("Width = item.Length;");
            code.Append("            table.Add(\"").Append(member.ColumnName).Append("\", ").Append(local).Append(", ").Append(local).AppendLine("Width);");
        }
        else
        {
            code.Append("            table.Add(\"").Append(member.ColumnName).Append("\", ").Append(local).AppendLine(");");
        }
    }

    private static void EmitKeywordWrites(StringBuilder code, TypeModel type, string variable)
    {
        foreach (var member in type.Members.Where(static item => item.Role == MemberRole.Keyword))
        {
            code.Append("                ").Append(WriteKeyword(member, variable)).AppendLine(";");
        }
    }

    private static string WriteKeyword(MemberModel member, string variable)
    {
        string value = variable + "." + member.Symbol.Name;
        return member.Symbol.Type.SpecialType switch
        {
            SpecialType.System_Boolean => $"header.Logical(\"{member.ColumnName}\", {value})",
            SpecialType.System_Int32 or SpecialType.System_Int16 => $"header.Integer(\"{member.ColumnName}\", {value})",
            SpecialType.System_Int64 => $"header.Integer(\"{member.ColumnName}\", {value})",
            SpecialType.System_Double or SpecialType.System_Single => $"header.Real(\"{member.ColumnName}\", {value})",
            SpecialType.System_String => $"header.Text(\"{member.ColumnName}\", {value} ?? \"\")",
            _ => $"header.Text(\"{member.ColumnName}\", {value}?.ToString() ?? \"\")",
        };
    }

    private static string ReadKeyword(MemberModel member)
    {
        string keyword = member.ColumnName;
        return member.Symbol.Type.SpecialType switch
        {
            SpecialType.System_Boolean => $"hdu.Header.GetBoolean(\"{keyword}\")",
            SpecialType.System_Int32 => $"(int)hdu.Header.GetInt64(\"{keyword}\")",
            SpecialType.System_Int16 => $"(short)hdu.Header.GetInt64(\"{keyword}\")",
            SpecialType.System_Int64 => $"hdu.Header.GetInt64(\"{keyword}\")",
            SpecialType.System_Double => $"hdu.Header.GetDouble(\"{keyword}\")",
            SpecialType.System_Single => $"(float)hdu.Header.GetDouble(\"{keyword}\")",
            SpecialType.System_String => $"hdu.Header.GetString(\"{keyword}\")",
            _ => $"hdu.Header.GetString(\"{keyword}\")",
        };
    }

    private static string ReadColumn(MemberModel member)
    {
        string column = $"table.Column(\"{member.ColumnName}\")";
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
            return "document.Hdus.First(hdu => hdu.Kind == CFITS.FitsHduKind.Image)";
        }

        return $"document.Hdus.First(hdu => hdu.Kind == CFITS.FitsHduKind.Image && hdu.Header.TryGetCard(\"EXTNAME\", out var extname) && extname.GetString() == \"{name}\")";
    }

    private static string FindTable(string? name)
    {
        string kinds = "hdu.Kind == CFITS.FitsHduKind.BinaryTable || hdu.Kind == CFITS.FitsHduKind.AsciiTable";
        if (string.IsNullOrEmpty(name))
        {
            return $"document.Hdus.First(hdu => {kinds})";
        }

        return $"document.Hdus.First(hdu => ({kinds}) && hdu.Header.TryGetCard(\"EXTNAME\", out var extname) && extname.GetString() == \"{name}\")";
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
