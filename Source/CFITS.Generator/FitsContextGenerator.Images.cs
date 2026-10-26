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
        string keywords = string.Join(NewLine, type.Members
            .Where(static member => member.Role == MemberRole.Keyword)
            .Select(static member => $"                {member.Symbol.Name} = {ReadKeyword(member, "hdu.Header")},"));
        var keywordMembers = type.Members.Where(static member => member.Role == MemberRole.Keyword).ToList();
        string directKeywords = string.Join(NewLine, keywordMembers.Select((member, index) =>
            $"                    {member.Symbol.Name} = {ReadDirectKeyword(member, index)},"));
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
            string keywordSpan = keywordMembers.Count == 0
                ? ""
                : string.Join(NewLine, keywordMembers.Select((member, index) =>
                    $"                        __cfitsKeywords[{index}] = new({Literal(member.ColumnName)}, {KeywordKind(member)});"));
            string keywordPrefix = keywordMembers.Count == 0
                ? ""
                : $$"""
                        var __cfitsKeywords = new CFITS.FitsKeywordQuery[{{keywordMembers.Count}}];
            {{keywordSpan}}
            """;
            string keywordArgument = keywordMembers.Count == 0 ? "" : ", __cfitsKeywords";
            directRead = $$"""
            {{keywordPrefix}}            if (!options.VerifyChecksum && stream.CanSeek && CFITS.FitsDirectImage.TryRead<{{element}}>(stream, {{directArray.Rank}}, {{type.HduIndex}}, {{nameArgument}}, options{{keywordArgument}}, out System.Array? __cfitsRaw))
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
