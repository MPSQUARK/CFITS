namespace CFITS.Tests.Serialization.Models;

public sealed class ExposureKeywords
{
    [FitsKeyword("EXPTIME")]
    [FitsRequired]
    public double ExposureSeconds { get; set; }
}
