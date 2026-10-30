using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CFITS.Generator;

public sealed partial class FitsContextGenerator
{
    private static string FindImage(string? name, int hduIndex, string kindCondition, bool required, string owner)
    {
        return FindHdu(name, hduIndex, kindCondition, required, owner);
    }

    private static string FindTable(string? name, int hduIndex, bool required, string owner)
    {
        string kinds = "candidate.Kind == CFITS.FitsHduKind.BinaryTable || candidate.Kind == CFITS.FitsHduKind.AsciiTable";
        return FindHdu(name, hduIndex, kinds, required, owner);
    }

    private static string FindHdu(string? name, int hduIndex, string kindCondition, bool required, string owner)
    {
        kindCondition = $"({kindCondition})";
        string match = string.IsNullOrEmpty(name)
            ? kindCondition
            : $"{kindCondition} && candidate.Header.TryGetCard(\"EXTNAME\", out var extname) && extname.GetString() == {Literal(name ?? "")}";
        string missing = Literal($"The HDU for '{owner}' was not found.");
        string incompatible = Literal($"The HDU for '{owner}' has an incompatible type.");
        string requiredThrow = required
            ? $$"""
                        if (hdu is null)
                        {
                            throw new CFITS.FitsException({{missing}});
                        }
                """
            : "";
        if (hduIndex >= 0)
        {
            return $$"""
                        CFITS.FitsHdu? hdu = null;
                        if (document.Hdus.Count > {{hduIndex}})
                        {
                            var candidate = document.Hdus[{{hduIndex}}];
                            if ({{match}})
                            {
                                hdu = candidate;
                            }
                            else
                            {
                                throw new CFITS.FitsException({{incompatible}});
                            }
                        }
            {{requiredThrow}}
            """;
        }

        return $$"""
                        CFITS.FitsHdu? hdu = null;
                        for (int index = 0; index < document.Hdus.Count; index++)
                        {
                            var candidate = document.Hdus[index];
                            if ({{match}})
                            {
                                hdu = candidate;
                                break;
                            }
                        }
            {{requiredThrow}}
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
