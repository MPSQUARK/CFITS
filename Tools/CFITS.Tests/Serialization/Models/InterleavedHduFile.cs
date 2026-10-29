namespace CFITS.Tests.Serialization.Models;

[FitsDocument(IgnoreOtherHdus = false)]
public sealed partial class InterleavedHduFile
{
    [FitsPrimaryHeader]
    [FitsRequired]
    public ExposureKeywords Keywords { get; set; } = null!;

    [FitsImage(0)]
    [FitsRequired]
    public ExposureImage Image { get; set; } = null!;

    [FitsTable(2, "EVENTS")]
    [FitsRequired]
    public EventTable Events { get; set; } = null!;

    [FitsOtherHdus]
    public IReadOnlyList<FitsHduSnapshot> Unknown { get; set; } = [];
}
