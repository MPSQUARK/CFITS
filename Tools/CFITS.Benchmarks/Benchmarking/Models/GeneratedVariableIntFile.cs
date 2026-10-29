namespace CFITS.Benchmarks.Benchmarking.Models;

[FitsDocument(IgnoreOtherHdus = true)]
public sealed partial class GeneratedVariableIntFile
{
    [FitsTable(1)]
    [FitsRequired]
    public GeneratedVariableIntTable Table { get; set; } = null!;
}
