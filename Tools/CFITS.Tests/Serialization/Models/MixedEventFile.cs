namespace CFITS.Tests.Serialization.Models;

[FitsDocument(IgnoreOtherHdus = true)]
public sealed partial class MixedEventFile
{
    [FitsTable(1, "MIXED_EVENTS")]
    [FitsRequired]
    public MixedEventTable Events { get; set; } = null!;
}
