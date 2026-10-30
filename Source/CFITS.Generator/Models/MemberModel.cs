using Microsoft.CodeAnalysis;

namespace CFITS.Generator.Models;

internal sealed class MemberModel
{
    public MemberModel(IPropertySymbol symbol, MemberRole role, string columnName, bool required)
    {
        Symbol = symbol;
        Role = role;
        ColumnName = columnName;
        Required = required;
    }

    public IPropertySymbol Symbol { get; }

    public MemberRole Role { get; }

    public string ColumnName { get; }

    public bool Required { get; }
}
