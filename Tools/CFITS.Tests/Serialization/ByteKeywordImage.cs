namespace CFITS.Tests;

[FitsImage("BYTE_KEYWORD")]
public sealed class ByteKeywordImage
{
    [FitsKeyword("LEVEL")]
    public byte BitDepth { get; set; }

    [FitsData]
    public byte[]? Pixels { get; set; }
}
