namespace CFITS.Tests.Serialization.Models;

[FitsDocument(IgnoreOtherHdus = true)]
public sealed partial class NamedEventFile
{
    [FitsTable(2, "EVENTS")]
    [FitsRequired]
    public EventTable Events { get; set; } = null!;
}
