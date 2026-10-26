namespace CFITS.Tests;

[FitsTable]
public sealed class VariableIntRow
{
    [FitsColumn("VALUES")]
    public int[] Values { get; set; } = [];
}
