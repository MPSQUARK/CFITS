namespace CFITS.Benchmarks.Models;

[FitsDocument(IgnoreOtherHdus = false)]
public sealed partial class GeneratedWholeFile
{
    [FitsOtherHdus]
    public IReadOnlyList<FitsHduSnapshot> Hdus { get; set; } = [];

    [FitsSpecialRecords]
    public ReadOnlyMemory<byte> SpecialRecords { get; set; }
}
