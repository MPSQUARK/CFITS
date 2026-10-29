namespace CFITS.Tests.Serialization.Models;

public sealed class VariableDoubleRow
{
    [FitsColumn("VALUES")]
    [FitsRequired]
    public double[] Values { get; set; } = [];
}
