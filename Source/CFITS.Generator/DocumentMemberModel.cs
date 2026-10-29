using Microsoft.CodeAnalysis;

namespace CFITS.Generator;

internal sealed class DocumentMemberModel
{
    public DocumentMemberModel(
        IPropertySymbol property,
        TypeModel? child,
        DocumentMemberRole role,
        bool required,
        INamedTypeSymbol? wrapper,
        string? rowProperty,
        bool assignHeader,
        KeywordObjectModel? keywordObject,
        List<MemberModel> keywords)
    {
        Property = property;
        Child = child;
        Role = role;
        Required = required;
        Wrapper = wrapper;
        RowProperty = rowProperty;
        AssignHeader = assignHeader;
        KeywordObject = keywordObject;
        Keywords = keywords;
    }

    public IPropertySymbol Property { get; }

    public TypeModel? Child { get; }

    public DocumentMemberRole Role { get; }

    public bool Required { get; }

    public INamedTypeSymbol? Wrapper { get; }

    public string? RowProperty { get; }

    public bool AssignHeader { get; }

    public KeywordObjectModel? KeywordObject { get; }

    public List<MemberModel> Keywords { get; }
}
