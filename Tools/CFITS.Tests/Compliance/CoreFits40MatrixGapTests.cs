using System.Text;

namespace CFITS.Tests;

/// <summary>NASA FITS 4.0 core card, image, and HDU sequencing regressions.</summary>
public sealed class CoreFits40MatrixGapTests
{
    [Fact]
    public void RejectsPrimaryExtendWithAnUndefinedValue()
    {
        var error = Assert.Throws<FitsException>(() => FitsDocument.Parse(Header(
            Card("SIMPLE", "= T"),
            Card("BITPIX", "= 8"),
            Card("NAXIS", "= 0"),
            Card("EXTEND", "= / missing"))));

        Assert.Equal("EXTEND", error.Keyword);
        Assert.Equal(0, error.HduIndex);
    }

    [Fact]
    public void RejectsExtendInAnExtensionHeader()
    {
        byte[] primary = Header(
            Card("SIMPLE", "= T"),
            Card("BITPIX", "= 8"),
            Card("NAXIS", "= 0"));
        byte[] extension = Header(
            Card("XTENSION", "= 'IMAGE   '"),
            Card("BITPIX", "= 8"),
            Card("NAXIS", "= 0"),
            Card("PCOUNT", "= 0"),
            Card("GCOUNT", "= 1"),
            Card("EXTEND", "= T"));

        var error = Assert.Throws<FitsException>(() => FitsDocument.Parse(primary.Concat(extension).ToArray()));

        Assert.Equal("EXTEND", error.Keyword);
        Assert.Equal(1, error.HduIndex);
    }

    [Fact]
    public void ReportsBitpixOverflowAtTheBitpixCard()
    {
        var error = Assert.Throws<FitsException>(() => FitsDocument.Parse(Header(
            Card("SIMPLE", "= T"),
            Card("BITPIX", "= 9223372036854775807"),
            Card("NAXIS", "= 0"))));

        Assert.Equal("BITPIX", error.Keyword);
        Assert.Equal(0, error.HduIndex);
        Assert.NotNull(error.Position);
    }

    [Fact]
    public void RejectsNonFiniteRealHeaderValues()
    {
        var error = Assert.Throws<FitsException>(() => FitsDocument.Parse(Header(
            Card("SIMPLE", "= T"),
            Card("BITPIX", "= 8"),
            Card("NAXIS", "= 0"),
            Card("BSCALE", "= 1E999"))));

        Assert.Equal("BSCALE", error.Keyword);
    }

    [Fact]
    public void AcceptsZeroLengthImageAxesWithoutData()
    {
        byte[] file = Header(
            Card("SIMPLE", "= T"),
            Card("BITPIX", "= 64"),
            Card("NAXIS", "= 2"),
            Card("NAXIS1", "= 0"),
            Card("NAXIS2", "= 2147483647"));

        using var document = FitsDocument.Parse(file);

        Assert.Equal(0, document.Hdus[0].Image!.Length);
        Assert.Equal(0, document.Hdus[0].RawData.Length);
    }

    [Fact]
    public void RejectsImageExtensionWithNonCanonicalMandatoryCounts()
    {
        byte[] primary = Header(
            Card("SIMPLE", "= T"),
            Card("BITPIX", "= 8"),
            Card("NAXIS", "= 0"));
        byte[] extension = Header(
            Card("XTENSION", "= 'IMAGE   '"),
            Card("BITPIX", "= 8"),
            Card("NAXIS", "= 0"),
            Card("PCOUNT", "= 1"),
            Card("GCOUNT", "= 2"));

        var error = Assert.Throws<FitsException>(() => FitsDocument.Parse(primary.Concat(extension).ToArray()));

        Assert.Equal(1, error.HduIndex);
        Assert.Contains("IMAGE", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("PCOUNT", error.Keyword);
    }

    [Fact]
    public void RejectsUnknownExtensionWithoutItsRequiredCounts()
    {
        byte[] primary = Header(
            Card("SIMPLE", "= T"),
            Card("BITPIX", "= 8"),
            Card("NAXIS", "= 0"));
        byte[] extension = Header(
            Card("XTENSION", "= 'CUSTOM'"),
            Card("BITPIX", "= 8"),
            Card("NAXIS", "= 0"));

        var error = Assert.Throws<FitsException>(() => FitsDocument.Parse(primary.Concat(extension).ToArray()));

        Assert.Equal(1, error.HduIndex);
        Assert.Equal("PCOUNT", error.Keyword);
        Assert.Equal(3200, error.Position);
    }

    [Theory]
    [InlineData("1e2")]
    [InlineData("1d2")]
    [InlineData("1E")]
    [InlineData("1D+")]
    [InlineData(".")]
    [InlineData("+")]
    public void RejectsNonConformingNumericValueGrammar(string value)
    {
        var error = Assert.Throws<FitsException>(() => FitsDocument.Parse(Header(
            Card("SIMPLE", "= T"),
            Card("BITPIX", "= 8"),
            Card("NAXIS", "= 0"),
            Card("VALUE", "= " + value))));

        Assert.Equal("VALUE", error.Keyword);
        Assert.Equal(240, error.Position);
    }

    [Fact]
    public void ReportsMalformedComplexValueAtItsCard()
    {
        var error = Assert.Throws<FitsException>(() => FitsDocument.Parse(Header(
            Card("SIMPLE", "= T"),
            Card("BITPIX", "= 8"),
            Card("NAXIS", "= 0"),
            Card("COMPLEX", "= (1,2"))));

        Assert.Equal("COMPLEX", error.Keyword);
        Assert.Equal(240, error.Position);
    }

    [Fact]
    public void AcceptsNormativeDateUndefinedComplexAndNumericBoundaries()
    {
        using var document = FitsDocument.Parse(Header(
            Card("SIMPLE", "= T"),
            Card("BITPIX", "= 8"),
            Card("NAXIS", "= 0"),
            Card("MININT", "= -9223372036854775808"),
            Card("MAXINT", "= +9223372036854775807"),
            Card("REAL", "= .1"),
            Card("REAL2", "= 1."),
            Card("REAL3", "= 1.E+2"),
            Card("DATE-OBS", "= '2026-10-26T12:06:00.123456789'"),
            Card("UNDEF", "= / intentionally undefined")));

        var header = document.Hdus[0].Header;
        Assert.Equal(long.MinValue, header.GetInt64("MININT"));
        Assert.Equal(long.MaxValue, header.GetInt64("MAXINT"));
        Assert.Equal(0.1, header.GetDouble("REAL"));
        Assert.Equal(100, header.GetDouble("REAL3"));
        Assert.Equal("2026-10-26T12:06:00.123456789", header.GetString("DATE-OBS"));
        Assert.True(header.TryGetCard("UNDEF", out var undefined));
        Assert.Equal(FitsValueKind.Undefined, undefined.Kind);
    }

    private static byte[] Header(params string[] cards)
    {
        byte[] header = Encoding.ASCII.GetBytes(string.Concat(cards.Append(Card("END", ""))));
        return header.Concat(Enumerable.Repeat((byte)' ', 2880 - header.Length)).ToArray();
    }

    private static string Card(string keyword, string value) =>
        (keyword.PadRight(8) + value).PadRight(80);
}
