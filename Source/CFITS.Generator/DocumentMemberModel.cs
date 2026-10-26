using Microsoft.CodeAnalysis;

namespace CFITS.Generator;

internal sealed class DocumentMemberModel
{
    public DocumentMemberModel(IPropertySymbol property, TypeModel? child, DocumentMemberRole role)
    {
        Property = property;
        Child = child;
        Role = role;
    }

    public IPropertySymbol Property { get; }

    public TypeModel? Child { get; }

    public DocumentMemberRole Role { get; }
}

