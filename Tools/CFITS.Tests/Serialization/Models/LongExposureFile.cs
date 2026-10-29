namespace CFITS.Tests.Serialization.Models;

[FitsDocument(IgnoreOtherHdus = true)]
public sealed partial class LongExposureFile
{
    [FitsImage("LONG_EXPOSURE")]
    [FitsRequired]
    public LongExposureImage Image { get; set; } = null!;
}
