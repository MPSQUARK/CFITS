namespace CFITS.Benchmarks.Models;

[FitsDocument(IgnoreOtherHdus = true)]
public sealed partial class GeneratedAsciiFile
{
    [FitsTable(1)]
    [FitsRequired]
    public GeneratedAsciiTable Table { get; set; } = null!;
}
