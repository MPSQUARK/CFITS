namespace CFITS.Benchmarks.Models;

[FitsDocument(IgnoreOtherHdus = true)]
public sealed partial class GeneratedExposureFile
{
    [FitsPrimaryHeader]
    [FitsRequired]
    public GeneratedExposureKeywords Keywords { get; set; } = null!;

    [FitsImage("EXPOSURE")]
    [FitsRequired]
    public GeneratedExposure Image { get; set; } = null!;
}
