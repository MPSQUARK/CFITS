namespace CFITS.Benchmarks.Benchmarking.Models;

[FitsDocument(IgnoreOtherHdus = true)]
public sealed partial class GeneratedVariableDoubleFile
{
    [FitsTable(1)]
    [FitsRequired]
    public GeneratedVariableDoubleTable Table { get; set; } = null!;
}
