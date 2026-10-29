namespace CFITS.Tests.Serialization.Models;

public sealed partial class LongExposureImage
{
    [FitsData]
    [FitsRequired]
    public long[]? Pixels { get; set; }
}
