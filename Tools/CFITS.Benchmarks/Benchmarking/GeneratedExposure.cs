namespace CFITS.Benchmarks;

[FitsImage]
public sealed class GeneratedExposure
{
    [FitsKeyword("EXPTIME")]
    public double ExposureSeconds { get; set; }

    [FitsData]
    public float[,]? Pixels { get; set; }
}
