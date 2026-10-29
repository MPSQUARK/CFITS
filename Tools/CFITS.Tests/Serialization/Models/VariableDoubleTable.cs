namespace CFITS.Tests.Serialization.Models;

public sealed partial class VariableDoubleTable
{
    public IReadOnlyList<VariableDoubleRow> Rows { get; set; } = [];
}
