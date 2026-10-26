namespace CFITS.Tests;

[FitsTable]
public sealed class VariableDoubleRow
{
    [FitsColumn("VALUES")]
    public double[] Values { get; set; } = [];
}
