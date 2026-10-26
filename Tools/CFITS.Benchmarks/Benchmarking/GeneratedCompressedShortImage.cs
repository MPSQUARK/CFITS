namespace CFITS.Benchmarks;

[FitsImage(HduIndex = 1)]
public sealed class GeneratedCompressedShortImage
{
    [FitsData]
    public short[,]? Pixels { get; set; }
}
