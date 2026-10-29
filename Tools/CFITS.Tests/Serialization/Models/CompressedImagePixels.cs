namespace CFITS.Tests.Serialization.Models;

public sealed partial class CompressedImagePixels
{
    [FitsData]
    [FitsRequired]
    public short[,]? Pixels { get; set; }
}
