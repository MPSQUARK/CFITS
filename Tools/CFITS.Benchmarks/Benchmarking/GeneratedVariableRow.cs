namespace CFITS.Benchmarks;

[FitsTable]
public sealed class GeneratedVariableRow
{
    [FitsColumn("samples")]
    public int[] Samples { get; set; } = [];
}
