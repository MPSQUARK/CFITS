namespace CFITS.Tests;

[FitsImage("SCALAR_IMAGE")]
public sealed class ScalarImage
{
    [FitsData]
    public float Pixel { get; set; }
}
