namespace CFITS.Benchmarks.Models;

public sealed partial class GeneratedCompressedShortImage
{
    [FitsData]
    [FitsRequired]
    public short[,]? Pixels { get; set; }
}
