namespace CFITS.Tests;

[FitsImage("UNSUPPORTED_IMAGE")]
public sealed class UnsupportedImage
{
    [FitsData]
    public bool[]? Pixels { get; set; }
}
