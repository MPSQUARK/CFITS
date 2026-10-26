using Microsoft.CodeAnalysis;

namespace CFITS.Generator;

internal sealed class MemberModel
{
    public MemberModel(IPropertySymbol symbol, MemberRole role, string columnName)
    {
        Symbol = symbol;
        Role = role;
        ColumnName = columnName;
    }

    public IPropertySymbol Symbol { get; }

    public MemberRole Role { get; }

    public string ColumnName { get; }
}

