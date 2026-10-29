namespace CFITS.Tests.Serialization.Models;

[FitsDocument(IgnoreOtherHdus = true)]
public sealed partial class ExposureFile
{
    [FitsPrimaryHeader]
    [FitsRequired]
    public ExposureKeywords Keywords { get; set; } = null!;

    [FitsImage("EXPOSURE")]
    [FitsRequired]
    public ExposureImage Image { get; set; } = null!;
}
