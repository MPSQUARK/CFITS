using System.Buffers.Binary;
using System.Text;

namespace CFITS.Tests;

public sealed class Fits40ScalingUnsignedComplianceTests
{
    [Fact]
    public void ReadsUnsignedSixteenBitBoundaryValuesAfterTheFITSOffset()
    {
        byte[] data = new byte[4];
        BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(0, 2), short.MinValue);
        BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(2, 2), short.MaxValue);
        byte[] file = Header(
            Card("SIMPLE", "= T"),
            Card("BITPIX", "= 16"),
            Card("NAXIS", "= 1"),
            Card("NAXIS1", "= 2"),
            Card("BSCALE", "= 1"),
            Card("BZERO", "= 32768"))
            .Concat(data)
            .Concat(Enumerable.Repeat((byte)0, 2876))
            .ToArray();

        using var document = FitsDocument.Parse(file);
        FitsImage image = document.Hdus[0].Image!;

        Assert.Equal(Int128.Zero, image.ReadInteger(0));
        Assert.Equal((Int128)65535, image.ReadInteger(1));
    }

    [Fact]
    public void RejectsZeroBscale()
    {
        byte[] file = Header(
            Card("SIMPLE", "= T"),
            Card("BITPIX", "= 16"),
            Card("NAXIS", "= 0"),
            Card("BSCALE", "= 0"));

        var error = Assert.Throws<FitsException>(() => FitsDocument.Parse(file));

        Assert.Contains("BSCALE", error.Message);
    }

    [Fact]
    public void RejectsABlankValueOutsideTheStoredIntegerDomain()
    {
        byte[] file = Header(
            Card("SIMPLE", "= T"),
            Card("BITPIX", "= 16"),
            Card("NAXIS", "= 1"),
            Card("NAXIS1", "= 1"),
            Card("BLANK", "= 65535"))
            .Concat(new byte[] { 0, 0 })
            .Concat(Enumerable.Repeat((byte)0, 2878))
            .ToArray();

        var error = Assert.Throws<FitsException>(() => FitsDocument.Parse(file));

        Assert.Contains("BLANK", error.Message);
    }

    private static byte[] Header(params string[] cards)
    {
        byte[] header = Encoding.ASCII.GetBytes(string.Concat(cards.Append(Card("END", ""))));
        return header.Concat(Enumerable.Repeat((byte)' ', 2880 - header.Length)).ToArray();
    }

    private static string Card(string keyword, string value) =>
        (keyword.PadRight(8) + value).PadRight(80);
}
