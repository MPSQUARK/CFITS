using Microsoft.CodeAnalysis;

namespace CFITS.Generator.Models;

internal sealed class KeywordObjectModel
{
    public KeywordObjectModel(IPropertySymbol property, INamedTypeSymbol symbol, List<MemberModel> keywords, bool required)
    {
        Property = property;
        Symbol = symbol;
        Keywords = keywords;
        Required = required;
    }

    public IPropertySymbol Property { get; }

    public INamedTypeSymbol Symbol { get; }

    public List<MemberModel> Keywords { get; }

    public bool Required { get; }
}
