using Microsoft.CodeAnalysis;

namespace CFITS.Generator;

internal sealed class TypeModel
{
    public TypeModel(
        INamedTypeSymbol symbol,
        bool table,
        string? hduName,
        string? writeName,
        int hduIndex,
        List<MemberModel> members,
        string infoName,
        bool hduRequired,
        bool materializeHeader,
        bool assignHeader,
        KeywordObjectModel? keywordObject,
        string ownerName)
    {
        Symbol = symbol;
        Table = table;
        HduName = hduName;
        WriteName = writeName;
        HduIndex = hduIndex;
        Members = members;
        InfoName = infoName;
        HduRequired = hduRequired;
        MaterializeHeader = materializeHeader;
        AssignHeader = assignHeader;
        KeywordObject = keywordObject;
        OwnerName = ownerName;
    }

    public INamedTypeSymbol Symbol { get; }

    public bool Table { get; }

    public string? HduName { get; }

    public string? WriteName { get; }

    public int HduIndex { get; }

    public List<MemberModel> Members { get; }

    public string InfoName { get; }

    public bool HduRequired { get; }

    public bool MaterializeHeader { get; }

    public bool AssignHeader { get; }

    public KeywordObjectModel? KeywordObject { get; }

    public string OwnerName { get; }
}
