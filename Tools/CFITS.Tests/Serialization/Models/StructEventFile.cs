namespace CFITS.Tests.Serialization.Models;

[FitsDocument(IgnoreOtherHdus = true)]
public sealed partial class StructEventFile
{
    [FitsTable(1)]
    [FitsRequired]
    public StructEventTable Events { get; set; } = null!;
}
