namespace CFITS.Tests;

[FitsDocumentModel]
public sealed class DirectCompositeFile
{
    [FitsImage(HduIndex = 0)]
    public Exposure Image { get; set; } = null!;

    [FitsTable(HduIndex = 1)]
    public IReadOnlyList<EventRow> Events { get; set; } = [];
}
