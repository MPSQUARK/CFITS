namespace CFITS.Benchmarks.Benchmarking.Models;

[FitsDocument(IgnoreOtherHdus = true)]
public sealed partial class GeneratedVariableFile
{
    [FitsTable(1)]
    [FitsRequired]
    public GeneratedVariableTable Table { get; set; } = null!;
}
