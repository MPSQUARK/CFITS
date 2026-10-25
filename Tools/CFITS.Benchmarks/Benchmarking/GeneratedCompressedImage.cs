namespace CFITS.Benchmarks;

[FitsImage(HduIndex = 1)]
public sealed class GeneratedCompressedImage
{
    [FitsData]
    public float[,]? Pixels { get; set; }
}
