namespace CFITS.Benchmarks.Benchmarking.Models;

[FitsDocument(IgnoreOtherHdus = true)]
public sealed partial class GeneratedShortImageFile
{
    [FitsImage]
    [FitsRequired]
    public GeneratedShortImage Image { get; set; } = null!;
}
