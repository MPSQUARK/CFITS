namespace CFITS.Benchmarks.Benchmarking.Models;

public sealed partial class GeneratedFloatImage
{
    [FitsData]
    [FitsRequired]
    public float[,]? Pixels { get; set; }
}
