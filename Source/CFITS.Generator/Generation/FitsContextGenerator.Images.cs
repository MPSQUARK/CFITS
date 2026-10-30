using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CFITS.Generator;

public sealed partial class FitsContextGenerator
{
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
        string keywords = EmitImageKeywords(type, "hdu.Header", "__cfits");
        string keywordObject = EmitImageKeywordObject(type, "hdu.Header", "__cfitsHeader");
        string headerProperty = type.AssignHeader ? "                Header = header!," : "";
        string dataAssignment = "";
        if (data is not null && data.Symbol.Type is IArrayTypeSymbol array)
        {
            dataAssignment = ReadArray(array);
        }

        string missingImage = data?.Required == true
            ? "throw new CFITS.FitsException(\"Image data is missing.\");"
            : $$"""
                return new {{name}}
                            {
                {{EmitImageKeywords(type, "hdu.Header", "__cfitsAbsent")}}
                {{EmitImageKeywordObject(type, "hdu.Header", "__cfitsAbsentHeader")}}
                {{headerProperty}}
                            };
                """;
        string write = data is not null && data.Symbol.Type is IArrayTypeSymbol written
            ? EmitImageWrite(type, written, data.Symbol.Name, data.Required)
            : $$"""
                        void WriteHeader(CFITS.FitsHeaderBuilder header)
                        {
                            overlay?.Invoke(header);
                {{EmitKeywordWrites(type, "value")}}
                        }
                        builder.Image(System.ReadOnlySpan<int>.Empty, System.ReadOnlySpan<byte>.Empty, WriteHeader);
            """;
        string directRead = "";
        if (hasReadableData && data?.Symbol.Type is IArrayTypeSymbol directArray)
        {
            directRead = EmitDirectImageRead(type, data, directArray);
        }

        string dataLine = hasReadableData ? $"                {data!.Symbol.Name} = __cfitsData," : "";
        return $$"""
                private sealed class {{type.InfoName}} : CFITS.FitsTypeInfo<{{name}}>
                {
                    public override {{name}} Read(System.IO.Stream stream)
                    {
                        return Read(stream, new CFITS.FitsOpenOptions());
                    }

                    public override {{name}} Read(System.IO.Stream stream, CFITS.FitsOpenOptions options)
                    {
                        return Read(stream, options, out _);
                    }

                    internal static {{name}} Read(System.IO.Stream stream, CFITS.FitsOpenOptions options, out CFITS.FitsHeader? header)
                    {
                        header = null;
            {{directRead}}            using var document = CFITS.FitsDocument.Open(stream, options);
                        return ReadDocument(document, null, out header);
                    }

                    internal static {{name}} ReadDocument(CFITS.FitsDocument document, bool[]? mapped, out CFITS.FitsHeader? header)
                    {
            {{FindImage(type.HduName, type.HduIndex, "candidate.Kind == CFITS.FitsHduKind.Image || candidate.Image is not null", type.HduRequired, type.OwnerName)}}
                        if (hdu is null)
                        {
                            header = null;
                            return default!;
                        }

                        header = hdu.Header;
                        if (mapped is not null) mapped[hdu.Index] = true;
                        var image = hdu.Image;
                        if (image is null)
                        {
                            {{missingImage}}
                        }
            {{dataAssignment}}
                        return new {{name}}
                        {
            {{keywords}}
            {{keywordObject}}
            {{headerProperty}}
            {{dataLine}}
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
                    internal static void WriteToBuilder(CFITS.FitsDocumentBuilder builder, {{name}} value, System.Action<CFITS.FitsHeaderBuilder>? overlay = null)
                    {
            {{write}}
                    }
                }
            """;
    }

    private static string EmitDirectImageRead(TypeModel type, MemberModel data, IArrayTypeSymbol array)
    {
        string element = ElementName(array.ElementType);
        string commas = array.Rank switch
        {
            1 => "",
            2 => ",",
            _ => ",,",
        };
        string nameArgument = string.IsNullOrEmpty(type.HduName) ? "null" : Literal(type.HduName!);
        string headerProperty = type.AssignHeader ? "                    Header = __cfitsHeader," : "";
        if (type.MaterializeHeader)
        {
            string keywords = EmitImageKeywords(type, "__cfitsHeader", "__cfitsDirect");
            string keywordObject = EmitImageKeywordObject(type, "__cfitsHeader", "__cfitsDirectHeader");
            return $$"""
                        if (!options.VerifyChecksum && stream.CanSeek && CFITS.FitsDirectImage.TryRead<{{element}}>(stream, {{array.Rank}}, {{type.HduIndex}}, {{nameArgument}}, options, out System.Array? __cfitsRaw, out header, false))
                        {
                            var __cfitsHeader = header!;
                            var __cfitsData = ({{element}}[{{commas}}])__cfitsRaw!;
                            return new {{type.Symbol.ToDisplayString()}}
                            {
            {{keywords}}
            {{keywordObject}}
            {{headerProperty}}                    {{data.Symbol.Name}} = __cfitsData,
                            };
                        }

            """;
        }

        var keywordMembers = type.Members.Where(static member => member.Role == MemberRole.Keyword).ToList();
        string directKeywords = string.Join(NewLine, keywordMembers.Select((member, index) =>
            $"                    {member.Symbol.Name} = {ReadDirectKeyword(member, index)},"));
        string keywordSpan = keywordMembers.Count == 0
            ? ""
            : string.Join(NewLine, keywordMembers.Select((member, index) =>
                $"                        __cfitsKeywords[{index}] = new({Literal(member.ColumnName)}, {KeywordKind(member)}) {{ Required = {(member.Required ? "true" : "false")} }};"));
        string keywordPrefix = keywordMembers.Count == 0
            ? ""
            : $$"""
                        var __cfitsKeywords = new CFITS.FitsKeywordQuery[{{keywordMembers.Count}}];
            {{keywordSpan}}
            """;
        string keywordArgument = keywordMembers.Count == 0 ? "" : ", __cfitsKeywords";
        return $$"""
            {{keywordPrefix}}
                        if (!options.VerifyChecksum && stream.CanSeek && CFITS.FitsDirectImage.TryRead<{{element}}>(stream, {{array.Rank}}, {{type.HduIndex}}, {{nameArgument}}, options{{keywordArgument}}, out System.Array? __cfitsRaw, false))
                        {
                            var __cfitsData = ({{element}}[{{commas}}])__cfitsRaw!;
                            return new {{type.Symbol.ToDisplayString()}}
                            {
            {{directKeywords}}{{(directKeywords.Length > 0 ? NewLine : "")}}                    {{data.Symbol.Name}} = __cfitsData,
                            };
                        }

            """;
    }

    private static string EmitImageKeywords(TypeModel type, string header, string prefix) =>
        string.Join(NewLine, type.Members
            .Where(static member => member.Role == MemberRole.Keyword)
            .Select(member => $"                {member.Symbol.Name} = {ReadKeyword(member, header, prefix)},"));

    private static string EmitImageKeywordObject(TypeModel type, string header, string prefix)
    {
        if (type.KeywordObject is not { } keywordObject)
        {
            return "";
        }

        string assignments = string.Join(NewLine, keywordObject.Keywords.Select(member =>
            $"                    {member.Symbol.Name} = {ReadKeyword(member, header, prefix)},"));
        return $$"""
                {{keywordObject.Property.Name}} = new {{keywordObject.Symbol.ToDisplayString()}}
                        {
            {{assignments}}
                        },
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

    private static string EmitImageWrite(TypeModel type, IArrayTypeSymbol array, string property, bool required)
    {
        if (array.Rank > 3 || !IsSupportedImageElement(array.ElementType))
        {
            return "            throw new System.NotSupportedException(\"Generated image serialization supports only byte, short, int, long, float, and double arrays up to rank 3.\");";
        }

        string extensionName = string.IsNullOrEmpty(type.WriteName)
            ? ""
            : $"                    header.Text(\"EXTNAME\", {Literal(type.WriteName!)});";
        string missing = required
            ? "throw new CFITS.FitsException(\"Image data is missing.\");"
            : "builder.Image(System.ReadOnlySpan<int>.Empty, System.ReadOnlySpan<byte>.Empty, WriteHeader);\n                        return;";
        string dimensions = array.Rank switch
        {
            1 => """
                        int n1 = pixels.Length;
                        builder.Image(new int[] { n1 }, pixels, WriteHeader);
                """,
            2 => """
                        int n2 = pixels.GetLength(0);
                        int n1 = pixels.GetLength(1);
                        builder.Image(new int[] { n1, n2 }, CFITS.FitsEndian.AsContiguous(pixels), WriteHeader);
                """,
            _ => """
                        int n3 = pixels.GetLength(0);
                        int n2 = pixels.GetLength(1);
                        int n1 = pixels.GetLength(2);
                        builder.Image(new int[] { n1, n2, n3 }, CFITS.FitsEndian.AsContiguous(pixels), WriteHeader);
                """,
        };
        return $$"""
                    void WriteHeader(CFITS.FitsHeaderBuilder header)
                    {
                        overlay?.Invoke(header);
            {{EmitKeywordWrites(type, "value")}}
            {{extensionName}}
                    }
                    var pixels = value.{{property}};
                    if (pixels is null)
                    {
                        {{missing}}
                    }
            {{dimensions}}
            """;
    }
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
}
