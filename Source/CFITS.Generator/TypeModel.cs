using Microsoft.CodeAnalysis;

namespace CFITS.Generator;

internal sealed class TypeModel
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

