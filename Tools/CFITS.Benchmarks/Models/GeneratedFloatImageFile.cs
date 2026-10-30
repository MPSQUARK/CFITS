namespace CFITS.Benchmarks.Models;

[FitsDocument(IgnoreOtherHdus = true)]
public sealed partial class GeneratedFloatImageFile
{
    [FitsImage]
    [FitsRequired]
    public GeneratedFloatImage Image { get; set; } = null!;
}
