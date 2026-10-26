namespace CFITS.Benchmarks;

[FitsTable]
public struct GeneratedVariableRow
{
    public GeneratedVariableRow()
    {
    }

    [FitsColumn("samples")]
    public int[] Samples { get; set; } = null!;
}
