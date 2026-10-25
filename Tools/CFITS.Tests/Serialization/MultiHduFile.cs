namespace CFITS.Tests;

[FitsDocumentModel]
public sealed class MultiHduFile
{
    [FitsImage(HduIndex = 0)]
    public Exposure Primary { get; set; } = null!;

    [FitsTable(HduIndex = 1)]
    public IReadOnlyList<EventRow> Events { get; set; } = [];

    [FitsUnknownHdus]
    public IReadOnlyList<FitsHduSnapshot> Unknown { get; set; } = [];

    [FitsSpecialRecords]
    public ReadOnlyMemory<byte> SpecialRecords { get; set; }
}
