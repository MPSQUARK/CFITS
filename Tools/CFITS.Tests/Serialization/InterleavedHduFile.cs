namespace CFITS.Tests;

[FitsDocumentModel]
public sealed class InterleavedHduFile
{
    [FitsImage(HduIndex = 0)]
    public Exposure Primary { get; set; } = null!;

    [FitsTable(HduIndex = 2)]
    public IReadOnlyList<EventRow> Events { get; set; } = [];

    [FitsUnknownHdus]
    public IReadOnlyList<FitsHduSnapshot> Unknown { get; set; } = [];
}
