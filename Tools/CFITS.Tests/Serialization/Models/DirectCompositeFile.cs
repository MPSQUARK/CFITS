namespace CFITS.Tests.Serialization.Models;

[FitsDocument(IgnoreOtherHdus = true)]
public sealed partial class DirectCompositeFile
{
    [FitsPrimaryHeader]
    [FitsRequired]
    public ExposureKeywords Keywords { get; set; } = null!;

    [FitsImage]
    [FitsRequired]
    public ExposureImage Image { get; set; } = null!;

    [FitsTable("EVENTS")]
    [FitsRequired]
    public EventTable Events { get; set; } = null!;
}
