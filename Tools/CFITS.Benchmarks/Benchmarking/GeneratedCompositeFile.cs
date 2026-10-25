namespace CFITS.Benchmarks;

[FitsDocumentModel]
public sealed class GeneratedCompositeFile
{
    [FitsImage(HduIndex = 0)]
    public GeneratedExposure Image { get; set; } = null!;

    [FitsTable(HduIndex = 1)]
    public IReadOnlyList<GeneratedEventRow> Events { get; set; } = [];
}
