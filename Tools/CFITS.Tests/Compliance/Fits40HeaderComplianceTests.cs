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

    private static byte[] File(params string[] cards)
    {
        byte[] header = Encoding.ASCII.GetBytes(string.Concat(cards.Append(Card("END", ""))));
        return header.Concat(Enumerable.Repeat((byte)' ', 2880 - header.Length)).ToArray();
    }

    private static string Card(string keyword, string value) =>
        (keyword.PadRight(8) + value).PadRight(80);
}
