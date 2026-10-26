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

    private static string EmitImage(TypeModel type)
    {
        string name = type.Symbol.ToDisplayString();
        var data = type.Members.FirstOrDefault(static member => member.Role == MemberRole.Data);
        if (data is not null
            && (data.Symbol.Type is not IArrayTypeSymbol supportedArray
                || supportedArray.Rank > 3
                || !IsSupportedImageElement(supportedArray.ElementType)))
        {
            return EmitUnsupportedImage(type, data);
        }

        bool hasReadableData = data?.Symbol.Type is IArrayTypeSymbol readableArray
            && readableArray.Rank <= 3
            && IsSupportedImageElement(readableArray.ElementType);
        string keywords = string.Join(NewLine, type.Members
            .Where(static member => member.Role == MemberRole.Keyword)
            .Select(static member => $"                {member.Symbol.Name} = {ReadKeyword(member, "hdu.Header")},"));
        string directKeywords = string.Join(NewLine, type.Members
            .Where(static member => member.Role == MemberRole.Keyword)
            .Select(static member => $"                    {member.Symbol.Name} = {ReadKeyword(member, "__cfitsHeader!")},"));
        string dataAssignment = "";
        if (data is not null && data.Symbol.Type is IArrayTypeSymbol array)
        {
            dataAssignment = ReadArray(array);
        }

        string write = data is not null && data.Symbol.Type is IArrayTypeSymbol written
            ? EmitImageWrite(type, written, data.Symbol.Name)
            : $$"""
                        builder.Image(System.ReadOnlySpan<int>.Empty, System.ReadOnlySpan<byte>.Empty, header =>
                        {
            {{EmitKeywordWrites(type, "value")}}
                        });
            """;
        string directRead = "";
        if (hasReadableData && data?.Symbol.Type is IArrayTypeSymbol directArray)
        {
            string element = ElementName(directArray.ElementType);
            string commas = directArray.Rank switch
            {
                1 => "",
                2 => ",",
                _ => ",,",
            };
            string nameArgument = string.IsNullOrEmpty(type.HduName) ? "null" : Literal(type.HduName!);
            directRead = $$"""
                        if (!options.VerifyChecksum && stream.CanSeek && CFITS.FitsDirectImage.TryRead<{{element}}>(stream, {{directArray.Rank}}, {{type.HduIndex}}, {{nameArgument}}, options, out System.Array? __cfitsRaw, out CFITS.FitsHeader? __cfitsHeader))
                        {
                            var __cfitsData = ({{element}}[{{commas}}])__cfitsRaw!;
                            return new {{name}}
                            {
            {{directKeywords}}{{(directKeywords.Length > 0 ? NewLine : "")}}                    {{data.Symbol.Name}} = __cfitsData,
                            };
                        }

            """;
        }
        return $$"""
                private sealed class {{type.InfoName}} : CFITS.FitsTypeInfo<{{name}}>
                {
                    public override {{name}} Read(System.IO.Stream stream)
                    {
                        return Read(stream, new CFITS.FitsOpenOptions());
                    }

                    public override {{name}} Read(System.IO.Stream stream, CFITS.FitsOpenOptions options)
                    {
            {{directRead}}            using var document = CFITS.FitsDocument.Open(stream, options);
                        return ReadDocument(document);
                    }

                    internal static {{name}} ReadDocument(CFITS.FitsDocument document, bool[]? mapped = null)
                    {
            {{FindImage(type.HduName, type.HduIndex, "candidate.Kind == CFITS.FitsHduKind.Image")}}
                        if (mapped is not null) mapped[hdu.Index] = true;
                        var image = hdu.Image ?? throw new CFITS.FitsException("The HDU is not an image.");
            {{dataAssignment}}
                        return new {{name}}
                        {
            {{keywords}}{{(keywords.Length > 0 && hasReadableData ? NewLine : "")}}{{(hasReadableData ? $"                {data!.Symbol.Name} = __cfitsData," : "")}}
                        };
                    }
                    public override void Write(System.IO.Stream stream, {{name}} value)
                    {
                        var builder = CFITS.FitsDocumentBuilder.Create();
                        Write(builder, value);
                        builder.Write(stream);
                    }
                    public override void Write(CFITS.FitsDocumentBuilder builder, {{name}} value)
                    {
                        WriteToBuilder(builder, value);
                    }
                    internal static void WriteToBuilder(CFITS.FitsDocumentBuilder builder, {{name}} value)
                    {
            {{write}}
                    }
                }
            """;
    }

    private static string EmitUnsupportedImage(TypeModel type, MemberModel member)
    {
        string name = type.Symbol.ToDisplayString();
        string message = member.Symbol.Type is IArrayTypeSymbol array && array.Rank > 3
            ? "Generated image serialization supports arrays up to rank 3."
            : member.Symbol.Type is IArrayTypeSymbol
                ? "Generated image serialization supports only byte, short, int, long, float, and double arrays."
                : $"Generated image serialization requires a supported array for [FitsData], but member '{member.Symbol.Name}' has type '{member.Symbol.Type.ToDisplayString()}'.";
        string literal = Literal(message);
        return $$"""
                private sealed class {{type.InfoName}} : CFITS.FitsTypeInfo<{{name}}>
                {
                    public override {{name}} Read(System.IO.Stream stream)
                    {
                        throw new System.NotSupportedException({{literal}});
                    }
                    public override void Write(System.IO.Stream stream, {{name}} value)
                    {
                        throw new System.NotSupportedException({{literal}});
                    }
                }
            """;
    }

    private static string EmitImageWrite(TypeModel type, IArrayTypeSymbol array, string property)
    {
        if (array.Rank > 3 || !IsSupportedImageElement(array.ElementType))
        {
            return "            throw new System.NotSupportedException(\"Generated image serialization supports only byte, short, int, long, float, and double arrays up to rank 3.\");";
        }

        string element = ElementName(array.ElementType);
        string extensionName = string.IsNullOrEmpty(type.HduName)
            ? ""
            : $"                header.Text(\"EXTNAME\", {Literal(type.HduName!)});";
        string dimensions;
        if (array.Rank == 1)
        {
            dimensions = $$"""
                        int n1 = pixels.Length;
                        builder.Image(new int[] { n1 }, pixels, header =>
                """;
        }
        else if (array.Rank == 2)
        {
            dimensions = $$"""
                        int n2 = pixels.GetLength(0);
                        int n1 = pixels.GetLength(1);
                        builder.Image(new int[] { n1, n2 }, CFITS.FitsEndian.AsContiguous(pixels), header =>
                """;
        }
        else
        {
            dimensions = $$"""
                        int n3 = pixels.GetLength(0);
                        int n2 = pixels.GetLength(1);
                        int n1 = pixels.GetLength(2);
                        builder.Image(new int[] { n1, n2, n3 }, CFITS.FitsEndian.AsContiguous(pixels), header =>
                """;
        }

        return $$"""
                    var pixels = value.{{property}} ?? throw new CFITS.FitsException("Image data is missing.");
            {{dimensions}}
                    {
            {{EmitKeywordWrites(type, "value")}}
            {{extensionName}}
                    });
            """;
    }

    private static string EmitTable(TypeModel type)
    {
        string name = type.Symbol.ToDisplayString();
        var columnMembers = type.Members.Where(static member => member.Role == MemberRole.Column).ToList();
        MemberModel? unsupported = type.Members.FirstOrDefault(static member =>
            member.Role is MemberRole.Column or MemberRole.Keyword
            && !IsSupportedTableMember(member.Symbol.Type));
        if (unsupported is not null)
        {
            return EmitUnsupportedTable(type, unsupported);
        }

        string cachedColumns = string.Join(NewLine, columnMembers.Select((member, index) =>
            $"                        var __cfitsColumn{index} = table.Column({Literal(member.ColumnName)});"));
        string bulkCapabilities = string.Join(NewLine, columnMembers
            .Select((member, index) => EmitBulkColumn(member, index))
            .Where(static text => text.Length > 0));
        string bulkRents = string.Join(NewLine, columnMembers
            .Select((member, index) => EmitBulkRent(member, index))
            .Where(static text => text.Length > 0));
        string bulkCopies = string.Join(NewLine, columnMembers
            .Select((member, index) => EmitBulkCopy(member, index))
            .Where(static text => text.Length > 0));
        string bulkReturns = string.Join(NewLine, columnMembers
            .Select((member, index) => EmitBulkReturn(member, index))
            .Where(static text => text.Length > 0));
        string scalarColumns = string.Join(NewLine, type.Members
            .Where(static member => member.Role == MemberRole.Column)
            .Select((member, index) => $"                    {member.Symbol.Name} = {ReadColumn(member, index, bulk: false)},"));
        string bulkColumnsRead = string.Join(NewLine, type.Members
            .Where(static member => member.Role == MemberRole.Column)
            .Select((member, index) => $"                    {member.Symbol.Name} = {ReadColumn(member, index, bulk: true)},"));
        string scalarLoop = string.Join(NewLine,
            "                            for (int row = 0; row < table.RowCount; row++)",
            "                            {",
            $"                                rows[row] = new {name}",
            "                                {",
            scalarColumns,
            "                                };",
            "                            }");
        string bulkLoop = string.Join(NewLine,
            "                            for (int row = 0; row < table.RowCount; row++)",
            "                            {",
            $"                                rows[row] = new {name}",
            "                                {",
            bulkColumnsRead,
            "                                };",
            "                            }");
        string rowLoops = bulkCapabilities.Length == 0
            ? scalarLoop
            : string.Join(NewLine,
                $"                        if (table.RowCount >= {BulkReadRowThreshold})",
                "                        {",
                bulkRents,
                "                            try",
                "                            {",
                bulkCopies,
                bulkLoop,
                "                            }",
                "                            finally",
                "                            {",
                bulkReturns,
                "                            }",
                "                        }",
                "                        else",
                "                        {",
                scalarLoop,
                "                        }");
        string writes = string.Join(NewLine, type.Members
            .Where(static member => member.Role == MemberRole.Column)
            .Select(EmitColumnWrite));
        string extensionName = string.IsNullOrEmpty(type.HduName)
            ? ""
            : $"                header.Text(\"EXTNAME\", {Literal(type.HduName!)});";
        string nameArgument = string.IsNullOrEmpty(type.HduName) ? "null" : Literal(type.HduName!);
        string variableHelpers = EmitVariableColumnHelpers(columnMembers);
        return $$"""
                private sealed class {{type.InfoName}} : CFITS.FitsTableTypeInfo<{{name}}>
                {
            {{variableHelpers}}
                    public override System.Collections.Generic.IReadOnlyList<{{name}}> Read(System.IO.Stream stream)
                    {
                        return Read(stream, new CFITS.FitsOpenOptions());
                    }

                    public override System.Collections.Generic.IReadOnlyList<{{name}}> Read(System.IO.Stream stream, CFITS.FitsOpenOptions options)
                    {
                        if (CFITS.FitsDirectTable.TryOpen(stream, {{type.HduIndex}}, {{nameArgument}}, options, out CFITS.FitsDirectTable? __cfitsDirect))
                        {
                            using (__cfitsDirect)
                            {
                                return ReadTable(__cfitsDirect!.Table);
                            }
                        }

                        using var document = CFITS.FitsDocument.Open(stream, options);
                        return ReadDocument(document);
                    }

                    internal static System.Collections.Generic.IReadOnlyList<{{name}}> ReadDocument(CFITS.FitsDocument document, bool[]? mapped = null)
                    {
            {{FindTable(type.HduName, type.HduIndex)}}
                        if (mapped is not null) mapped[hdu.Index] = true;
                        return ReadTable(hdu.Table ?? throw new CFITS.FitsException("The HDU is not a table."));
                    }

                    internal static System.Collections.Generic.IReadOnlyList<{{name}}> ReadTable(CFITS.FitsTable table)
                    {
                        var rows = new {{name}}[table.RowCount];
            {{cachedColumns}}
            {{bulkCapabilities}}
            {{rowLoops}}
                        return rows;
                    }
                    public override void Write(System.IO.Stream stream, System.Collections.Generic.IReadOnlyList<{{name}}> rows)
                    {
                        var builder = CFITS.FitsDocumentBuilder.Create();
                        Write(builder, rows);
                        builder.Write(stream);
                    }
                    public override void Write(CFITS.FitsDocumentBuilder builder, System.Collections.Generic.IReadOnlyList<{{name}}> rows)
                    {
                        WriteToBuilder(builder, rows);
                    }
                    internal static void WriteToBuilder(CFITS.FitsDocumentBuilder builder, System.Collections.Generic.IReadOnlyList<{{name}}> rows)
                    {
                        var table = new CFITS.BinaryTableBuilder();
            {{writes}}
                        builder.BinaryTable(table, header =>
                        {
            {{extensionName}}
                        });
                    }
                }
            """;
    }

    private static string EmitDocument(DocumentModel document, IReadOnlyList<TypeModel> knownTypes)
    {
        string root = document.Symbol.ToDisplayString();
        bool hasUnknown = document.Members.Any(static member => member.Role == DocumentMemberRole.Unknown);
        bool canWriteChildrenDirectly = CanWriteChildrenDirectly(document);
        string mappedArgument = hasUnknown ? "mapped" : "null";
        string members = string.Join(NewLine, document.Members.Select(member =>
        {
            string assignment = member.Role switch
            {
                DocumentMemberRole.Image =>
                    $"{member.Property.Name} = {member.Child!.InfoName}.ReadDocument(document, {mappedArgument}),",
                DocumentMemberRole.Table =>
                    $"{member.Property.Name} = {member.Child!.InfoName}.ReadDocument(document, {mappedArgument}),",
                DocumentMemberRole.Unknown =>
                    $"{member.Property.Name} = document.SnapshotHdus(mapped!),",
                DocumentMemberRole.SpecialRecords =>
                    $"{member.Property.Name} = document.SnapshotSpecialRecords(),",
                _ => throw new InvalidOperationException(),
            };
            return $"                {assignment}";
        }));
        string helpers = string.Join(NewLine, document.Members
            .Where(static member => member.Child is not null)
            .Select(member => member.Child!.Table ? EmitTable(member.Child) : EmitImage(member.Child)));
        string unknownUse = hasUnknown
            ? "            var mapped = new bool[document.Hdus.Count];"
            : "";
        string writes = string.Join(NewLine, document.Members
            .Where(static member => member.Child is not null)
            .Select((member, index) =>
            {
                if (member.Child!.HduIndex < 0)
                {
                    return $"                        throw new System.InvalidOperationException(\"The mapped HDU for property '{member.Property.Name}' has no explicit HduIndex; name-based composite writes cannot preserve HDU order.\");";
                }

                return $$"""
                        var mappedDocument{{index}} = CFITS.FitsDocumentBuilder.Create();
                        {{(member.Child.HduIndex > 0 ? $"                        mappedDocument{index}.EnsurePrimaryHdu();" : "")}}
                        {{member.Child.InfoName}}.WriteToBuilder(mappedDocument{{index}}, value.{{member.Property.Name}});
                        using var mappedResult{{index}} = mappedDocument{{index}}.Build();
                        var mappedHdu{{index}} = mappedResult{{index}}.SnapshotHdus()[^1].WithIndex({{member.Child.HduIndex}});
                        ordered.Add((mappedHdu{{index}}.Index, mappedHdu{{index}}));
                """;
            }));
        string unknownWrites = document.Members.Any(static member => member.Role == DocumentMemberRole.Unknown)
            ? $"                        foreach (var snapshot in value.{document.Members.First(static member => member.Role == DocumentMemberRole.Unknown).Property.Name}) ordered.Add((snapshot.Index, snapshot));"
            : "";
        string specialWrite = document.Members.FirstOrDefault(static member => member.Role == DocumentMemberRole.SpecialRecords) is { } specialMember
            ? $"                        builder.SpecialRecords(value.{specialMember.Property.Name});"
            : "";
        string writeBody = canWriteChildrenDirectly
            ? EmitDirectDocumentWrite(document)
            : $$"""
                        var ordered = new System.Collections.Generic.List<(int Index, CFITS.FitsHduSnapshot Snapshot)>();
            {{writes}}
            {{unknownWrites}}
                        ordered.Sort(static (left, right) => left.Index.CompareTo(right.Index));
                        foreach (var item in ordered) builder.Hdu(item.Snapshot);
            {{specialWrite}}
            """;
        return $$"""
                private sealed class {{document.Symbol.Name}}Info : CFITS.FitsDocumentTypeInfo<{{root}}>
                {
                    public override {{root}} Read(CFITS.FitsDocument document)
                    {
                        {{unknownUse}}
                        return new {{root}}
                        {
            {{members}}
                        };
                    }

                    public override void Write(System.IO.Stream stream, {{root}} value)
                    {
                        var builder = CFITS.FitsDocumentBuilder.Create();
                        Write(builder, value);
                        builder.Write(stream);
                    }

                    public override void Write(CFITS.FitsDocumentBuilder builder, {{root}} value)
                    {
                        System.ArgumentNullException.ThrowIfNull(builder);
            {{writeBody}}
                    }
                }
            {{helpers}}
        """;
    }

    private static bool CanWriteChildrenDirectly(DocumentModel document)
    {
        if (document.Members.Any(static member =>
                member.Role is DocumentMemberRole.Unknown or DocumentMemberRole.SpecialRecords))
        {
            return false;
        }

        var children = document.Members
            .Where(static member => member.Child is not null)
            .Select(static member => member.Child!)
            .OrderBy(static child => child.HduIndex)
            .ToArray();
        return children.Length > 0
            && children.Select(static child => child.HduIndex).SequenceEqual(Enumerable.Range(0, children.Length));
    }

    private static string EmitDirectDocumentWrite(DocumentModel document)
    {
        var children = document.Members
            .Where(static member => member.Child is not null)
            .OrderBy(static member => member.Child!.HduIndex)
            .ToArray();
        var lines = new List<string>();
        foreach (DocumentMemberModel member in children)
        {
            if (member.Child!.HduIndex > 0)
            {
                lines.Add("                        builder.EnsurePrimaryHdu();");
            }

            lines.Add($"                        {member.Child.InfoName}.WriteToBuilder(builder, value.{member.Property.Name});");
        }

        return string.Join(NewLine, lines);
    }

    private static string EmitVariableColumnHelpers(IReadOnlyList<MemberModel> members)
    {
        bool hasInteger = members.Any(static member =>
            member.Symbol.Type is IArrayTypeSymbol array
            && array.ElementType.SpecialType == SpecialType.System_Int32);
        bool hasDouble = members.Any(static member =>
            member.Symbol.Type is IArrayTypeSymbol array
            && array.ElementType.SpecialType == SpecialType.System_Double);
        bool hasFloat = members.Any(static member =>
            member.Symbol.Type is IArrayTypeSymbol array
            && array.ElementType.SpecialType == SpecialType.System_Single);
        var helpers = new StringBuilder();
        if (hasInteger)
        {
            helpers.AppendLine("""
                    private static int[] ReadInt32Array(CFITS.FitsColumn column, int row)
                    {
                        var values = new int[column.Count(row)];
                        column.CopyIntegers(row, values);
                        return values;
                    }
                """);
        }

        if (hasDouble)
        {
            helpers.AppendLine("""
                    private static double[] ReadDoubleArray(CFITS.FitsColumn column, int row)
                    {
                        return column.ReadRealVector(row);
                    }
                """);
        }

        if (hasFloat)
        {
            helpers.AppendLine("""
                    private static float[] ReadSingleArray(CFITS.FitsColumn column, int row)
                    {
                        var values = new float[column.Count(row)];
                        for (int index = 0; index < values.Length; index++)
                        {
                            values[index] = (float)column.ReadReal(row, index);
                        }

                        return values;
                    }
                """);
        }

        return helpers.ToString();
    }

    private static string EmitColumnWrite(MemberModel member)
    {
        string local = member.Symbol.Name.ToLowerInvariant() + "Values";
        if (member.Symbol.Type is IArrayTypeSymbol array)
        {
            string arrayElement = ElementName(array.ElementType);
            return $$"""
                        var {{local}} = new {{arrayElement}}[rows.Count][];
                        for (int index = 0; index < rows.Count; index++)
                        {
                            {{local}}[index] = rows[index].{{member.Symbol.Name}};
                        }
                        table.AddVariable({{Literal(member.ColumnName)}}, {{local}});
            """;
        }

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

    private static string EmitUnsupportedTable(TypeModel type, MemberModel member)
    {
        string name = type.Symbol.ToDisplayString();
        string message = Literal($"Generated table serialization does not support member '{member.ColumnName}' of type '{member.Symbol.Type.ToDisplayString()}'.");
        return $$"""
                private sealed class {{type.InfoName}} : CFITS.FitsTableTypeInfo<{{name}}>
                {
                    public override System.Collections.Generic.IReadOnlyList<{{name}}> Read(System.IO.Stream stream)
                    {
                        throw new System.NotSupportedException({{message}});
                    }
                    public override void Write(System.IO.Stream stream, System.Collections.Generic.IReadOnlyList<{{name}}> rows)
                    {
                        throw new System.NotSupportedException({{message}});
                    }
                }
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
            SpecialType.System_Byte or SpecialType.System_Int32 or SpecialType.System_Int16 => $"header.Integer({Literal(member.ColumnName)}, {value})",
            SpecialType.System_Int64 => $"header.Integer({Literal(member.ColumnName)}, {value})",
            SpecialType.System_Double or SpecialType.System_Single => $"header.Real({Literal(member.ColumnName)}, {value})",
            SpecialType.System_String => $"header.Text({Literal(member.ColumnName)}, {value} ?? \"\")",
            _ => $"header.Text({Literal(member.ColumnName)}, {value}?.ToString() ?? \"\")",
        };
    }

    private static string ReadKeyword(MemberModel member, string header)
    {
        string keyword = member.ColumnName;
        return member.Symbol.Type.SpecialType switch
        {
            SpecialType.System_Boolean => $"{header}.GetBoolean({Literal(keyword)})",
            SpecialType.System_Byte => $"(byte){header}.GetInt64({Literal(keyword)})",
            SpecialType.System_Int32 => $"(int){header}.GetInt64({Literal(keyword)})",
            SpecialType.System_Int16 => $"(short){header}.GetInt64({Literal(keyword)})",
            SpecialType.System_Int64 => $"{header}.GetInt64({Literal(keyword)})",
            SpecialType.System_Double => $"{header}.GetDouble({Literal(keyword)})",
            SpecialType.System_Single => $"(float){header}.GetDouble({Literal(keyword)})",
            SpecialType.System_String => $"{header}.GetString({Literal(keyword)})",
            _ => $"{header}.GetString({Literal(keyword)})",
        };
    }

    private static string ReadColumn(MemberModel member, int index, bool bulk = true)
    {
        string column = $"__cfitsColumn{index}";
        string integer = $"__cfitsInteger{index}![row]";
        string real = $"__cfitsReal{index}![row]";
        if (member.Symbol.Type is IArrayTypeSymbol array)
        {
            return array.ElementType.SpecialType switch
            {
                SpecialType.System_Double => $"ReadDoubleArray({column}, row)",
                SpecialType.System_Single => $"ReadSingleArray({column}, row)",
                _ => $"ReadInt32Array({column}, row)",
            };
        }

        return member.Symbol.Type.SpecialType switch
        {
            SpecialType.System_Boolean => $"{column}.ReadLogical(row)",
            SpecialType.System_Int32 => bulk ? $"(int)(__cfitsInteger{index}Bulk ? {integer} : {column}.ReadInteger(row))" : $"(int){column}.ReadInteger(row)",
            SpecialType.System_Int16 => bulk ? $"(short)(__cfitsInteger{index}Bulk ? {integer} : {column}.ReadInteger(row))" : $"(short){column}.ReadInteger(row)",
            SpecialType.System_Int64 => bulk ? $"(__cfitsInteger{index}Bulk ? {integer} : {column}.ReadInteger(row))" : $"{column}.ReadInteger(row)",
            SpecialType.System_Byte => bulk ? $"(byte)(__cfitsInteger{index}Bulk ? {integer} : {column}.ReadInteger(row))" : $"(byte){column}.ReadInteger(row)",
            SpecialType.System_Double => bulk ? $"(__cfitsReal{index}Bulk ? {real} : {column}.ReadReal(row))" : $"{column}.ReadReal(row)",
            SpecialType.System_Single => bulk ? $"(__cfitsReal{index}Bulk ? (float){real} : (float){column}.ReadReal(row))" : $"(float){column}.ReadReal(row)",
            SpecialType.System_String => $"{column}.ReadString(row)",
            _ => $"{column}.ReadString(row)",
        };
    }

    private static string EmitBulkColumn(MemberModel member, int index)
    {
        return member.Symbol.Type.SpecialType switch
        {
            SpecialType.System_Int32 or SpecialType.System_Int16 or SpecialType.System_Int64 or SpecialType.System_Byte =>
                $"                        bool __cfitsInteger{index}Bulk = __cfitsColumn{index}.CanCopyInteger;",
            SpecialType.System_Double or SpecialType.System_Single =>
                $"                        bool __cfitsReal{index}Bulk = __cfitsColumn{index}.CanCopyReal;",
            _ => "",
        };
    }

    private static string EmitBulkRent(MemberModel member, int index) =>
        member.Symbol.Type.SpecialType switch
        {
            SpecialType.System_Int32 =>
                $"                            int[]? __cfitsInteger{index} = __cfitsInteger{index}Bulk ? System.Buffers.ArrayPool<int>.Shared.Rent(table.RowCount) : null;",
            SpecialType.System_Int16 =>
                $"                            short[]? __cfitsInteger{index} = __cfitsInteger{index}Bulk ? System.Buffers.ArrayPool<short>.Shared.Rent(table.RowCount) : null;",
            SpecialType.System_Int64 =>
                $"                            long[]? __cfitsInteger{index} = __cfitsInteger{index}Bulk ? System.Buffers.ArrayPool<long>.Shared.Rent(table.RowCount) : null;",
            SpecialType.System_Byte =>
                $"                            byte[]? __cfitsInteger{index} = __cfitsInteger{index}Bulk ? System.Buffers.ArrayPool<byte>.Shared.Rent(table.RowCount) : null;",
            SpecialType.System_Double =>
                $"                            double[]? __cfitsReal{index} = __cfitsReal{index}Bulk ? System.Buffers.ArrayPool<double>.Shared.Rent(table.RowCount) : null;",
            SpecialType.System_Single =>
                $"                            float[]? __cfitsReal{index} = __cfitsReal{index}Bulk ? System.Buffers.ArrayPool<float>.Shared.Rent(table.RowCount) : null;",
            _ => "",
        };

    private static string EmitBulkCopy(MemberModel member, int index) =>
        member.Symbol.Type.SpecialType switch
        {
            SpecialType.System_Int32 =>
                $"                                if (__cfitsInteger{index}Bulk && !__cfitsColumn{index}.TryCopyInteger(__cfitsInteger{index}!.AsSpan(0, table.RowCount))) throw new CFITS.FitsException(\"The column changed bulk-copy capability.\");",
            SpecialType.System_Int16 =>
                $"                                if (__cfitsInteger{index}Bulk && !__cfitsColumn{index}.TryCopyInteger(__cfitsInteger{index}!.AsSpan(0, table.RowCount))) throw new CFITS.FitsException(\"The column changed bulk-copy capability.\");",
            SpecialType.System_Int64 =>
                $"                                if (__cfitsInteger{index}Bulk && !__cfitsColumn{index}.TryCopyInteger(__cfitsInteger{index}!.AsSpan(0, table.RowCount))) throw new CFITS.FitsException(\"The column changed bulk-copy capability.\");",
            SpecialType.System_Byte =>
                $"                                if (__cfitsInteger{index}Bulk && !__cfitsColumn{index}.TryCopyInteger(__cfitsInteger{index}!.AsSpan(0, table.RowCount))) throw new CFITS.FitsException(\"The column changed bulk-copy capability.\");",
            SpecialType.System_Double =>
                $"                                if (__cfitsReal{index}Bulk && !__cfitsColumn{index}.TryCopyReal(__cfitsReal{index}!.AsSpan(0, table.RowCount))) throw new CFITS.FitsException(\"The column changed bulk-copy capability.\");",
            SpecialType.System_Single =>
                $"                                if (__cfitsReal{index}Bulk && !__cfitsColumn{index}.TryCopyReal(__cfitsReal{index}!.AsSpan(0, table.RowCount))) throw new CFITS.FitsException(\"The column changed bulk-copy capability.\");",
            _ => "",
        };

    private static string EmitBulkReturn(MemberModel member, int index) =>
        member.Symbol.Type.SpecialType switch
        {
            SpecialType.System_Int32 =>
                $"                                if (__cfitsInteger{index} is not null) System.Buffers.ArrayPool<int>.Shared.Return(__cfitsInteger{index});",
            SpecialType.System_Int16 =>
                $"                                if (__cfitsInteger{index} is not null) System.Buffers.ArrayPool<short>.Shared.Return(__cfitsInteger{index});",
            SpecialType.System_Int64 =>
                $"                                if (__cfitsInteger{index} is not null) System.Buffers.ArrayPool<long>.Shared.Return(__cfitsInteger{index});",
            SpecialType.System_Byte =>
                $"                                if (__cfitsInteger{index} is not null) System.Buffers.ArrayPool<byte>.Shared.Return(__cfitsInteger{index});",
            SpecialType.System_Double =>
                $"                                if (__cfitsReal{index} is not null) System.Buffers.ArrayPool<double>.Shared.Return(__cfitsReal{index});",
            SpecialType.System_Single =>
                $"                                if (__cfitsReal{index} is not null) System.Buffers.ArrayPool<float>.Shared.Return(__cfitsReal{index});",
            _ => "",
        };

    private static string BulkCapability(MemberModel member, int index) =>
        member.Symbol.Type.SpecialType switch
        {
            SpecialType.System_Int32 or SpecialType.System_Int16 or SpecialType.System_Int64 or SpecialType.System_Byte =>
                $"__cfitsInteger{index}Bulk",
            SpecialType.System_Double or SpecialType.System_Single =>
                $"__cfitsReal{index}Bulk",
            _ => "false",
        };

    private static string ReadArray(IArrayTypeSymbol array)
    {
        string element = ElementName(array.ElementType);
        string cast = array.ElementType.SpecialType == SpecialType.System_Double ? "" : $"({element})";
        bool integer = IsIntegerImageElement(array.ElementType);
        bool bulk = array.ElementType.SpecialType is SpecialType.System_Single or SpecialType.System_Double;
        string copy = integer ? "CopyInteger" : "CopyPhysical";
        if (array.Rank == 1)
        {
            string validation = ImageRankValidation(1);
            if (bulk || integer)
            {
                return validation
                    + $"                            var __cfitsData = new {element}[image.Length];{NewLine}"
                    + $"                            image.{copy}(__cfitsData);";
            }

            return validation
                + $"                            var __cfitsData = new {element}[image.Length];{NewLine}"
                + $"                            for (int i = 0; i < image.Length; i++){NewLine}"
                + $"                            {{{NewLine}"
                + $"                                __cfitsData[i] = {(integer ? $"{cast}image.ReadInteger(i)" : $"{cast}image.ReadPhysical(i)")};{NewLine}"
                + "                            }";
        }

        if (array.Rank == 2)
        {
            string validation = ImageRankValidation(2);
            if (bulk || integer)
            {
                return validation
                    + "                            int n1 = image.Axes[0];\n"
                    + "                            int n2 = image.Axes[1];\n"
                    + $"                            var __cfitsData = new {element}[n2, n1];\n"
                    + $"                            image.{copy}(__cfitsData);";
            }

            return $$"""
            {{validation}}
                            int n1 = image.Axes[0];
                            int n2 = image.Axes[1];
                            var __cfitsData = new {{element}}[n2, n1];
                            for (int y = 0; y < n2; y++)
                            {
                                for (int x = 0; x < n1; x++)
                                {
                                    __cfitsData[y, x] = {{(integer ? $"{cast}image.ReadInteger(x + (y * n1))" : $"{cast}image.ReadPhysical(x + (y * n1))")}};
                                }
                            }
            """;
        }

        if (bulk || integer)
        {
            return ImageRankValidation(3)
                + "                            int n1 = image.Axes[0];\n"
                + "                            int n2 = image.Axes[1];\n"
                + "                            int n3 = image.Axes[2];\n"
                + $"                            var __cfitsData = new {element}[n3, n2, n1];\n"
                + $"                            image.{copy}(__cfitsData);";
        }

        return $$"""
        {{ImageRankValidation(3)}}
                            int n1 = image.Axes[0];
                            int n2 = image.Axes[1];
                            int n3 = image.Axes[2];
                            var __cfitsData = new {{element}}[n3, n2, n1];
                            for (int z = 0; z < n3; z++)
                            {
                                for (int y = 0; y < n2; y++)
                                {
                                    for (int x = 0; x < n1; x++)
                                    {
                                        __cfitsData[z, y, x] = {{(integer ? $"{cast}image.ReadInteger(x + n1 * (y + (n2 * z)))" : $"{cast}image.ReadPhysical(x + n1 * (y + (n2 * z)))")}};
                                    }
                                }
                            }
        """;
    }

    private static string ImageRankValidation(int rank) =>
        $"                            if (image.Axes.Count != {rank}) throw new CFITS.FitsException(\"Generated image rank does not match the target array.\");\n";

    private static bool IsSupportedImageElement(ITypeSymbol type) =>
        IsIntegerImageElement(type)
        || type.SpecialType is SpecialType.System_Single or SpecialType.System_Double;

    private static bool IsIntegerImageElement(ITypeSymbol type) =>
        type.SpecialType is SpecialType.System_Byte
            or SpecialType.System_Int16
            or SpecialType.System_Int32
            or SpecialType.System_Int64;

    private static bool IsSupportedTableMember(ITypeSymbol type) =>
        type is IArrayTypeSymbol array
            && array.Rank == 1
            && array.ElementType.SpecialType is (SpecialType.System_Int32 or SpecialType.System_Single or SpecialType.System_Double)
        || type.SpecialType is SpecialType.System_Boolean
            or SpecialType.System_Byte
            or SpecialType.System_Int16
            or SpecialType.System_Int32
            or SpecialType.System_Int64
            or SpecialType.System_Single
            or SpecialType.System_Double
            or SpecialType.System_String;

    private static string FindImage(string? name, int hduIndex, string kindCondition)
    {
        return FindHdu(name, hduIndex, kindCondition);
    }

    private static string FindTable(string? name, int hduIndex)
    {
        string kinds = "candidate.Kind == CFITS.FitsHduKind.BinaryTable || candidate.Kind == CFITS.FitsHduKind.AsciiTable";
        return FindHdu(name, hduIndex, kinds);
    }

    private static string FindHdu(string? name, int hduIndex, string kindCondition)
    {
        kindCondition = $"({kindCondition})";
        string match = string.IsNullOrEmpty(name)
            ? kindCondition
            : $"{kindCondition} && candidate.Header.TryGetCard(\"EXTNAME\", out var extname) && extname.GetString() == {Literal(name ?? "")}";
        if (hduIndex >= 0)
        {
            string indexedMatch = match.Replace("candidate", "hdu");
            return $$"""
                        if (document.Hdus.Count <= {{hduIndex}})
                        {
                            throw new System.InvalidOperationException("The requested HDU does not exist.");
                        }
                        CFITS.FitsHdu hdu = document.Hdus[{{hduIndex}}];
                        if (!({{indexedMatch}}))
                        {
                            throw new System.InvalidOperationException("The requested HDU has an incompatible type.");
                        }
            """;
        }

        return $$"""
                        CFITS.FitsHdu hdu = null!;
                        for (int index = 0; index < document.Hdus.Count; index++)
                        {
                            var candidate = document.Hdus[index];
                            if ({{match}})
                            {
                                hdu = candidate;
                                break;
                            }
                        }
                        if (hdu is null)
                        {
                            throw new System.InvalidOperationException("Sequence contains no matching element");
                        }
            """;
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
        public ContextModel(INamedTypeSymbol symbol, List<TypeModel> types, List<DocumentModel> documents)
        {
            Symbol = symbol;
            Types = types;
            Documents = documents;
        }

        public INamedTypeSymbol Symbol { get; }

        public List<TypeModel> Types { get; }

        public List<DocumentModel> Documents { get; }
    }

    private sealed class DocumentModel
    {
        public DocumentModel(INamedTypeSymbol symbol, List<DocumentMemberModel> members)
        {
            Symbol = symbol;
            Members = members;
        }

        public INamedTypeSymbol Symbol { get; }

        public List<DocumentMemberModel> Members { get; }
    }

    private sealed class DocumentMemberModel
    {
        public DocumentMemberModel(IPropertySymbol property, TypeModel? child, DocumentMemberRole role)
        {
            Property = property;
            Child = child;
            Role = role;
        }

        public IPropertySymbol Property { get; }

        public TypeModel? Child { get; }

        public DocumentMemberRole Role { get; }
    }

    private sealed class TypeModel
    {
        public TypeModel(
            INamedTypeSymbol symbol,
            bool table,
            string? hduName,
            int hduIndex,
            List<MemberModel> members,
            string? infoName = null)
        {
            Symbol = symbol;
            Table = table;
            HduName = hduName;
            HduIndex = hduIndex;
            Members = members;
            InfoName = infoName ?? $"{symbol.Name}Info";
        }

        public INamedTypeSymbol Symbol { get; }

        public bool Table { get; }

        public string? HduName { get; }

        public int HduIndex { get; }

        public List<MemberModel> Members { get; }

        public string InfoName { get; }
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

    private enum DocumentMemberRole
    {
        Image,
        Table,
        Unknown,
        SpecialRecords,
    }
}
