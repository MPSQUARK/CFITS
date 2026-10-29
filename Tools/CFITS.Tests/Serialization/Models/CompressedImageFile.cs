namespace CFITS.Tests.Serialization.Models;

[FitsDocument(IgnoreOtherHdus = true)]
public sealed partial class CompressedImageFile
{
    [FitsImage(1)]
    [FitsRequired]
    public CompressedImagePixels Image { get; set; } = null!;
}
