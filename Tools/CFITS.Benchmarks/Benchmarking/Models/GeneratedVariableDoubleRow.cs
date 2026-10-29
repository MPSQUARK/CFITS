namespace CFITS.Benchmarks.Benchmarking.Models;

public sealed class GeneratedVariableDoubleRow
{
    [FitsColumn("VALUES")]
    [FitsRequired]
    public double[] Values { get; set; } = [];
}
