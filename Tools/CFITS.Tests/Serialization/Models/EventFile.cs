namespace CFITS.Tests.Serialization.Models;

[FitsDocument(IgnoreOtherHdus = true)]
public sealed partial class EventFile
{
    [FitsTable(1, "EVENTS")]
    [FitsRequired]
    public EventTable Events { get; set; } = null!;
}
