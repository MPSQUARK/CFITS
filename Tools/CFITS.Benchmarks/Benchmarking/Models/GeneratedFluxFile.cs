namespace CFITS.Benchmarks.Benchmarking.Models;

[FitsDocument(IgnoreOtherHdus = true)]
public sealed partial class GeneratedFluxFile
{
    [FitsTable(1)]
    [FitsRequired]
    public GeneratedFluxTable Table { get; set; } = null!;
}
