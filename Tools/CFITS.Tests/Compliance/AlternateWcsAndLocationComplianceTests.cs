using System.Text;

namespace CFITS.Tests;

public sealed class AlternateWcsAndLocationComplianceTests
{
    [Fact]
    public void EvaluatesAnAlternateLinearWcsWithoutChangingThePrimaryWcs()
    {
        FitsHeader header = FitsHeader.Builder()
            .Integer("WCSAXES", 1)
            .Real("CRPIX1", 1)
            .Real("CRVAL1", 10)
            .Real("CDELT1", 2)
            .Integer("WCSAXESA", 1)
            .Real("CRPIX1A", 1)
            .Real("CRVAL1A", 100)
            .Real("CDELT1A", 5)
            .Text("CTYPE1A", "FREQ")
            .Build();

        Assert.Equal(12, FitsWorld.FromHeader(header).PixelToWorld([2])[0]);
        Assert.Equal(105, FitsWorld.FromHeader(header, 'A').PixelToWorld([2])[0]);
    }

    [Fact]
    public void ReportsTheMalformedExtensionHduInLocationContext()
    {
        byte[] primary = Header(
            Card("SIMPLE", "= T"),
            Card("BITPIX", "= 8"),
            Card("NAXIS", "= 0"));
        byte[] extension = Header(
            Card("XTENSION", "= 'BINTABLE'"),
            Card("BITPIX", "= 8"),
            Card("NAXIS", "= 2"),
            Card("NAXIS1", "= 1"),
            Card("NAXIS2", "= 1"),
            Card("PCOUNT", "= 0"),
            Card("GCOUNT", "= 1"));

        FitsException error = Assert.Throws<FitsException>(() =>
            FitsDocument.Parse(primary.Concat(extension).ToArray()));

        Assert.Equal(1, error.HduIndex);
        Assert.Contains("data array", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] Header(params string[] cards)
    {
        byte[] header = Encoding.ASCII.GetBytes(string.Concat(cards.Append(Card("END", ""))));
        return header.Concat(Enumerable.Repeat((byte)' ', 2880 - header.Length)).ToArray();
    }

    private static string Card(string keyword, string value) =>
        (keyword.PadRight(8) + value).PadRight(80);
}
