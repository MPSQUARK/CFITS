namespace CFITS.Benchmarks.Models;

[FitsDocument(IgnoreOtherHdus = true)]
public sealed partial class GeneratedCompressedShortImageFile
{
    [FitsImage(1)]
    [FitsRequired]
    public GeneratedCompressedShortImage Image { get; set; } = null!;
}
