using System.Buffers;

namespace CFITS.Tests;

[FitsImage]
public sealed class Exposure
{
    [FitsKeyword("EXPTIME")]
    public double ExposureSeconds { get; set; }

    [FitsData]
    public float[,]? Pixels { get; set; }
}
