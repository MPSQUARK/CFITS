using System.Text;

namespace CFITS.Tests;

public sealed class Fits40ImageExtensionComplianceTests
{
    [Fact]
    public void RejectsAnImageExtensionWithStructuralKeywordsOutOfOrder()
    {
        byte[] primary = Header(
            Card("SIMPLE", "= T"),
            Card("BITPIX", "= 8"),
            Card("NAXIS", "= 0"));
        byte[] extension = Header(
            Card("XTENSION", "= 'IMAGE   '"),
            Card("NAXIS", "= 0"),
            Card("BITPIX", "= 8"),
            Card("PCOUNT", "= 0"),
            Card("GCOUNT", "= 1"));

        var error = Assert.Throws<FitsException>(() => FitsDocument.Parse(primary.Concat(extension).ToArray()));

        Assert.Contains("order", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsBlankForFloatingPointImages()
    {
        byte[] file = Header(
            Card("SIMPLE", "= T"),
            Card("BITPIX", "= -32"),
            Card("NAXIS", "= 1"),
            Card("NAXIS1", "= 1"),
            Card("BLANK", "= 0"))
            .Concat(new byte[] { 0x3F, 0x80, 0x00, 0x00 })
            .Concat(Enumerable.Repeat((byte)0, 2876))
            .ToArray();

        var error = Assert.Throws<FitsException>(() => FitsDocument.Parse(file));

        Assert.Contains("BLANK", error.Message);
    }

    [Fact]
    public void RejectsAnImageExtensionWithAnIllegalBitpixAndNaxisCombination()
    {
        byte[] primary = Header(
            Card("SIMPLE", "= T"),
            Card("BITPIX", "= 8"),
            Card("NAXIS", "= 0"));
        byte[] extension = Header(
            Card("XTENSION", "= 'IMAGE   '"),
            Card("BITPIX", "= 0"),
            Card("NAXIS", "= 0"),
            Card("PCOUNT", "= 0"),
            Card("GCOUNT", "= 1"));

        var error = Assert.Throws<FitsException>(() => FitsDocument.Parse(primary.Concat(extension).ToArray()));

        Assert.Contains("BITPIX", error.Message);
    }

    private static byte[] Header(params string[] cards)
    {
        byte[] header = Encoding.ASCII.GetBytes(string.Concat(cards.Append(Card("END", ""))));
        return header.Concat(Enumerable.Repeat((byte)' ', 2880 - header.Length)).ToArray();
    }

    private static string Card(string keyword, string value) =>
        (keyword.PadRight(8) + value).PadRight(80);
}
