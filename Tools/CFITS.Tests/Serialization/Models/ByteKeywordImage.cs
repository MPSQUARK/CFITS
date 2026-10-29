namespace CFITS.Tests.Serialization.Models;

public sealed partial class ByteKeywordImage
{
    [FitsData]
    [FitsRequired]
    public byte[]? Pixels { get; set; }
}
