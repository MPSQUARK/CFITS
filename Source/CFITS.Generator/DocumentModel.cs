using Microsoft.CodeAnalysis;

namespace CFITS.Generator;

internal sealed class DocumentModel
{
    public DocumentModel(INamedTypeSymbol symbol, List<DocumentMemberModel> members)
    {
        Symbol = symbol;
        Members = members;
    }

    public INamedTypeSymbol Symbol { get; }

    public List<DocumentMemberModel> Members { get; }
}

