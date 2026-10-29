namespace CFITS.Tests.Serialization.Models;

public sealed class ByteKeywords
{
    [FitsKeyword("LEVEL")]
    [FitsRequired]
    public byte BitDepth { get; set; }
}
