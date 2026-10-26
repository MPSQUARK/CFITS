using System.Text;

namespace CFITS.Tests;

public sealed class Fits40HeaderComplianceTests
{
    [Fact]
    public void RejectsPrimaryStructuralKeywordsOutOfRequiredOrder()
    {
        byte[] file = File(
            Card("SIMPLE", "= T"),
            Card("NAXIS", "= 0"),
            Card("BITPIX", "= 8"));

        var error = Assert.Throws<FitsException>(() => FitsDocument.Parse(file));

        Assert.Contains("order", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsDuplicateAxisKeywords()
    {
        byte[] file = File(
            Card("SIMPLE", "= T"),
            Card("BITPIX", "= 8"),
            Card("NAXIS", "= 1"),
            Card("NAXIS1", "= 1"),
            Card("NAXIS1", "= 1"));

        var error = Assert.Throws<FitsException>(() => FitsDocument.Parse(file));

        Assert.Contains("NAXIS1", error.Message);
    }

    [Fact]
    public void RejectsAnAxisKeywordWhenNaxisIsZero()
    {
        byte[] file = File(
            Card("SIMPLE", "= T"),
            Card("BITPIX", "= 8"),
            Card("NAXIS", "= 0"),
            Card("NAXIS1", "= 1"));

        var error = Assert.Throws<FitsException>(() => FitsDocument.Parse(file));

        Assert.Contains("NAXIS1", error.Message);
    }

    [Fact]
    public void RejectsARequiredKeywordWithAnUndefinedValue()
    {
        byte[] file = File(
            Card("SIMPLE", "= T"),
            Card("BITPIX", "= "),
            Card("NAXIS", "= 0"));

        var error = Assert.Throws<FitsException>(() => FitsDocument.Parse(file));

        Assert.Contains("BITPIX", error.Message);
    }

    [Fact]
    public void RejectsSIMPLEInAnExtensionHeader()
    {
        byte[] primary = File(
            Card("SIMPLE", "= T"),
            Card("BITPIX", "= 8"),
            Card("NAXIS", "= 0"));
        byte[] extension = File(
            Card("XTENSION", "= 'IMAGE   '"),
            Card("SIMPLE", "= T"),
            Card("BITPIX", "= 8"),
            Card("NAXIS", "= 0"),
            Card("PCOUNT", "= 0"),
            Card("GCOUNT", "= 1"));

        var error = Assert.Throws<FitsException>(() => FitsDocument.Parse(primary.Concat(extension).ToArray()));

        Assert.Contains("SIMPLE", error.Message);
    }

    [Fact]
    public void RejectsBinaryTableWithoutItsMandatoryColumnKeywords()
    {
        byte[] primary = File(
            Card("SIMPLE", "= T"),
            Card("BITPIX", "= 8"),
            Card("NAXIS", "= 0"));
        byte[] extension = File(
            Card("XTENSION", "= 'BINTABLE'"),
            Card("BITPIX", "= 8"),
            Card("NAXIS", "= 2"),
            Card("NAXIS1", "= 1"),
            Card("NAXIS2", "= 0"),
            Card("PCOUNT", "= 0"),
            Card("GCOUNT", "= 1"));

        var error = Assert.Throws<FitsException>(() => FitsDocument.Parse(primary.Concat(extension).ToArray()));

        Assert.Contains("TFIELDS", error.Message);
    }

    [Fact]
    public void RejectsTableExtensionWithTheWrongNaxisRank()
    {
        byte[] primary = File(
            Card("SIMPLE", "= T"),
            Card("BITPIX", "= 8"),
            Card("NAXIS", "= 0"));
        byte[] extension = File(
            Card("XTENSION", "= 'BINTABLE'"),
            Card("BITPIX", "= 8"),
            Card("NAXIS", "= 1"),
            Card("NAXIS1", "= 1"),
            Card("PCOUNT", "= 0"),
            Card("GCOUNT", "= 1"),
            Card("TFIELDS", "= 1"),
            Card("TFORM1", "= '1B'"))
            .Concat(Enumerable.Repeat((byte)0, 2880))
            .ToArray();

        var error = Assert.Throws<FitsException>(() => FitsDocument.Parse(primary.Concat(extension).ToArray()));

        Assert.Contains("NAXIS", error.Message);
    }

    private static byte[] File(params string[] cards)
    {
        byte[] header = Encoding.ASCII.GetBytes(string.Concat(cards.Append(Card("END", ""))));
        return header.Concat(Enumerable.Repeat((byte)' ', 2880 - header.Length)).ToArray();
    }

    private static string Card(string keyword, string value) =>
        (keyword.PadRight(8) + value).PadRight(80);
}
