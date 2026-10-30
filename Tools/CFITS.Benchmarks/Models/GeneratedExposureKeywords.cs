namespace CFITS.Benchmarks.Models;

public sealed class GeneratedExposureKeywords
{
    [FitsKeyword("EXPTIME")]
    [FitsRequired]
    public double ExposureSeconds { get; set; }
}
