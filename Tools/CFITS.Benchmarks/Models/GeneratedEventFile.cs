namespace CFITS.Benchmarks.Models;

[FitsDocument(IgnoreOtherHdus = true)]
public sealed partial class GeneratedEventFile
{
    [FitsTable(1, "EVENTS")]
    [FitsRequired]
    public GeneratedEventTable Events { get; set; } = null!;
}
