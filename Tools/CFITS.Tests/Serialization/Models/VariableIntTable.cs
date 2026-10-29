namespace CFITS.Tests.Serialization.Models;

public sealed partial class VariableIntTable
{
    public IReadOnlyList<VariableIntRow> Rows { get; set; } = [];
}
