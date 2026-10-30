namespace CFITS.Benchmarks.Models;

public sealed class GeneratedVariableIntRow
{
    [FitsColumn("VALUES")]
    [FitsRequired]
    public int[] Values { get; set; } = [];
}
