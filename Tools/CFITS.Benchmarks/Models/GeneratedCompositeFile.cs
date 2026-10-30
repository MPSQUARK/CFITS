namespace CFITS.Benchmarks.Models;

[FitsDocument(IgnoreOtherHdus = true)]
public sealed partial class GeneratedCompositeFile
{
    [FitsPrimaryHeader]
    [FitsRequired]
    public GeneratedExposureKeywords Keywords { get; set; } = null!;

    [FitsImage("EXPOSURE")]
    [FitsRequired]
    public GeneratedExposure Image { get; set; } = null!;

    [FitsTable("EVENTS")]
    [FitsRequired]
    public GeneratedEventTable Events { get; set; } = null!;
}
