namespace CFITS.Benchmarks.Benchmarking.Models;

public sealed class GeneratedExposureKeywords
{
    [FitsKeyword("EXPTIME")]
    [FitsRequired]
    public double ExposureSeconds { get; set; }
}
