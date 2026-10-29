namespace CFITS.Tests.Serialization.Models;

[FitsDocument(IgnoreOtherHdus = false)]
public sealed partial class MultiHduFile
{
    [FitsPrimaryHeader]
    [FitsRequired]
    public ExposureKeywords Keywords { get; set; } = null!;

    [FitsImage]
    [FitsRequired]
    public ExposureImage Image { get; set; } = null!;

    [FitsTable("EVENTS")]
    [FitsRequired]
    public EventTable Events { get; set; } = null!;

    [FitsOtherHdus]
    public IReadOnlyList<FitsHduSnapshot> Unknown { get; set; } = [];

    [FitsSpecialRecords]
    public ReadOnlyMemory<byte> SpecialRecords { get; set; }
}
