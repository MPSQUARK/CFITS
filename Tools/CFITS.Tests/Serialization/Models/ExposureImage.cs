namespace CFITS.Tests.Serialization.Models;

public sealed partial class ExposureImage
{
    [FitsData]
    [FitsRequired]
    public float[,]? Pixels { get; set; }
}
