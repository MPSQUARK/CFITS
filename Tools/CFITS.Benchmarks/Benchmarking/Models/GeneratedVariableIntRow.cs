namespace CFITS.Benchmarks.Benchmarking.Models;

public sealed class GeneratedVariableIntRow
{
    [FitsColumn("VALUES")]
    [FitsRequired]
    public int[] Values { get; set; } = [];
}
