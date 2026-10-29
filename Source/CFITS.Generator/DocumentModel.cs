using Microsoft.CodeAnalysis;

namespace CFITS.Generator;

internal sealed class DocumentModel
{
    public DocumentModel(
        INamedTypeSymbol symbol,
        string accessibility,
        string kind,
        List<DocumentMemberModel> members,
        bool ignoreOtherHdus,
        bool keepHeaders,
        KeywordObjectModel? primaryKeywords,
        List<INamedTypeSymbol> headerTargets)
    {
        Symbol = symbol;
        Accessibility = accessibility;
        Kind = kind;
        Members = members;
        IgnoreOtherHdus = ignoreOtherHdus;
        KeepHeaders = keepHeaders;
        PrimaryKeywords = primaryKeywords;
        HeaderTargets = headerTargets;
    }

    public INamedTypeSymbol Symbol { get; }

    public string Accessibility { get; }

    public string Kind { get; }

    public List<DocumentMemberModel> Members { get; }

    public bool IgnoreOtherHdus { get; }

    public bool KeepHeaders { get; }

    public KeywordObjectModel? PrimaryKeywords { get; }

    public List<INamedTypeSymbol> HeaderTargets { get; }
}
