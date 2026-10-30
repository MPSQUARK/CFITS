namespace CFITS.Benchmarks.Models;

public sealed partial class GeneratedExposure
{
    [FitsData]
    [FitsRequired]
    public float[,]? Pixels { get; set; }
}
