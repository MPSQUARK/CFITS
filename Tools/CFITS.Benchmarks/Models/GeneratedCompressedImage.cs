namespace CFITS.Benchmarks.Models;

public sealed partial class GeneratedCompressedImage
{
    [FitsData]
    [FitsRequired]
    public float[,]? Pixels { get; set; }
}
