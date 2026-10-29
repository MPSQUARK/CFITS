namespace CFITS.Benchmarks.Benchmarking.Models;

public sealed partial class GeneratedShortImage
{
    [FitsData]
    [FitsRequired]
    public short[,]? Pixels { get; set; }
}
