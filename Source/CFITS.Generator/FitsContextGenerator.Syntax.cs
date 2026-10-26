using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CFITS.Generator;

public sealed partial class FitsContextGenerator
{
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
}
