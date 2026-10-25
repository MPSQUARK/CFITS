namespace CFITS.Benchmarks;

[FitsDocumentModel]
public sealed class GeneratedWholeFile
{
    [FitsUnknownHdus]
    public IReadOnlyList<FitsHduSnapshot> Hdus { get; set; } = [];

    [FitsSpecialRecords]
    public ReadOnlyMemory<byte> SpecialRecords { get; set; }
}
