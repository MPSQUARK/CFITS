namespace CFITS.Benchmarks.Models;

[FitsDocument(IgnoreOtherHdus = true)]
public sealed partial class GeneratedCompressedImageFile
{
    [FitsImage(1)]
    [FitsRequired]
    public GeneratedCompressedImage Image { get; set; } = null!;
}
