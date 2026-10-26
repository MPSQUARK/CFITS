using Microsoft.CodeAnalysis;

namespace CFITS.Generator;

internal sealed class ContextModel
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

