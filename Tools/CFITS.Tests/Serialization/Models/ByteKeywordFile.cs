namespace CFITS.Tests.Serialization.Models;

[FitsDocument(IgnoreOtherHdus = true)]
public sealed partial class ByteKeywordFile
{
    [FitsPrimaryHeader]
    [FitsRequired]
    public ByteKeywords Keywords { get; set; } = null!;

    [FitsImage("BYTE_KEYWORD")]
    [FitsRequired]
    public ByteKeywordImage Image { get; set; } = null!;
}
