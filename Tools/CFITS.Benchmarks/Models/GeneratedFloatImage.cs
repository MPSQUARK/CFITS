namespace CFITS.Benchmarks.Models;

public sealed partial class GeneratedFloatImage
{
    [FitsData]
    [FitsRequired]
    public float[,]? Pixels { get; set; }
}
