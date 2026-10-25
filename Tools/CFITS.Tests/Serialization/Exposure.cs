namespace CFITS.Tests;

[FitsImage("EXPOSURE")]
public sealed class Exposure
{
    [FitsKeyword("EXPTIME")]
    public double ExposureSeconds { get; set; }

    [FitsData]
    public float[,]? Pixels { get; set; }
}
