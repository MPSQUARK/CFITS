namespace CFITS.Benchmarks.Benchmarking.Models;

public struct GeneratedVariableRow
{
    public GeneratedVariableRow()
    {
    }

    [FitsColumn("samples")]
    [FitsRequired]
    public int[] Samples { get; set; } = null!;
}
