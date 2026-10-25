using System.Text;

namespace CFITS.Tests;

public sealed class HeaderSpecTests
{
    [Fact]
    public void HeaderCardsAndBlocksUseTheRequiredGeometry()
    {
        byte[] file = new FitsFileBuilder()
            .Card("SIMPLE", "T")
            .Card("BITPIX", "8")
            .Card("NAXIS", "0")
            .Build();

        Assert.Equal(2880, file.Length);
        Assert.Equal(80, Encoding.ASCII.GetString(file, 0, 80).Length);
        Assert.Equal("END", Encoding.ASCII.GetString(file, 240, 3));
        Assert.All(file[320..], value => Assert.Equal((byte)' ', value));
    }

    [Fact]
    public void ParsesLegalLogicalNumericComplexStringAndUndefinedValues()
    {
        byte[] file = new FitsFileBuilder()
            .Card("SIMPLE", "T")
            .Card("BITPIX", "8")
            .Card("NAXIS", "0")
            .Card("FALSE", "F")
            .Card("INTEGER", "-42")
            .Card("REAL", "1.5E+2")
            .Card("DOUBLE", "1.25D-08")
            .Card("COMPLEX", "(1.5,-2.5)")
            .Text("TEXT", "O'HARA")
            .Card("UNDEF", "")
            .Build();

        using var document = FitsDocument.Parse(file);
        var header = document.Hdus[0].Header;

        Assert.False(header.GetBoolean("FALSE"));
        Assert.Equal(-42, header.GetInt64("INTEGER"));
        Assert.Equal(150, header.GetDouble("REAL"));
        Assert.Equal(1.25e-8, header.GetDouble("DOUBLE"));
        Assert.True(header.TryGetCard("COMPLEX", out var complex));
        Assert.Equal(new FitsComplex(1.5, -2.5), complex.GetComplex());
        Assert.Equal("O'HARA", header.GetString("TEXT"));
        Assert.True(header.TryGetCard("UNDEF", out var undefined));
        Assert.Equal(FitsValueKind.Undefined, undefined.Kind);
        Assert.Null(undefined.GetString());
    }

    [Fact]
    public void ParsesDoubledQuotesAndIgnoresCardPadding()
    {
        byte[] file = Header(
            Card("SIMPLE", "= T"),
            Card("BITPIX", "= 8"),
            Card("NAXIS", "= 0"),
            Card("TEXT", "= 'O''HARA  '"));

        using var document = FitsDocument.Parse(file);

        Assert.Equal("O'HARA", document.Hdus[0].Header.GetString("TEXT"));
    }

    [Fact]
    public void CombinesContinuedLongStringCards()
    {
        byte[] file = Header(
            Card("SIMPLE", "= T"),
            Card("BITPIX", "= 8"),
            Card("NAXIS", "= 0"),
            Card("LONGSTR", "= 'first''part&'"),
            Card("CONTINUE", "  'second&'"),
            Card("CONTINUE", "  'third' / final"));

        using var document = FitsDocument.Parse(file);
        var header = document.Hdus[0].Header;

        Assert.Equal("first'partsecondthird", header.GetString("LONGSTR"));
        Assert.Single(header.Cards, card => card.Keyword == "LONGSTR");
    }

    [Fact]
    public void RetainsCommentaryAndUndefinedPhysicalCards()
    {
        byte[] file = Header(
            Card("SIMPLE", "= T"),
            Card("BITPIX", "= 8"),
            Card("NAXIS", "= 0"),
            Card("COMMENT", "a comment"),
            Card("HISTORY", "a history"),
            Card("", "a blank commentary"),
            Card("UNDEF", "= "));

        using var document = FitsDocument.Parse(file);
        var cards = document.Hdus[0].Header.Cards;

        Assert.Contains(cards, card => card is { Keyword: "COMMENT", Kind: FitsValueKind.Commentary, Text: "a comment" });
        Assert.Contains(cards, card => card is { Keyword: "HISTORY", Kind: FitsValueKind.Commentary, Text: "a history" });
        Assert.Contains(cards, card => card is { Keyword: "", Kind: FitsValueKind.Commentary, Text: "a blank commentary" });
        Assert.Contains(cards, card => card is { Keyword: "UNDEF", Kind: FitsValueKind.Undefined });
    }

    [Fact]
    public void RejectsNonAsciiHeaderRecords()
    {
        byte[] file = Header(
            Card("SIMPLE", "= T"),
            Card("BITPIX", "= 8"),
            Card("NAXIS", "= 0"));
        file[80 + 10] = 0x7f;

        var error = Assert.Throws<FitsException>(() => FitsDocument.Parse(file));

        Assert.Contains("ASCII", error.Message);
    }

    [Fact]
    public void RejectsNonAsciiTextWhenEncodingAHeader()
    {
        var error = Assert.Throws<FitsException>(() => FitsDocumentBuilder.Create()
            .Image(Array.Empty<int>(), Array.Empty<byte>(), header => header.Text("TEXT", "café"))
            .Build());

        Assert.Contains("ASCII", error.Message);
    }

    [Fact]
    public void RejectsAHeaderShorterThanOneCard()
    {
        byte[] file = Encoding.ASCII.GetBytes("SIMPLE  =                    T".PadRight(79));

        var error = Assert.Throws<FitsException>(() => FitsDocument.Parse(file));

        Assert.IsType<FitsException>(error);
    }

    [Fact]
    public void RejectsAHeaderWithoutEnd()
    {
        byte[] file = HeaderWithoutEnd(
            Card("SIMPLE", "= T"),
            Card("BITPIX", "= 8"),
            Card("NAXIS", "= 0"));

        var error = Assert.Throws<FitsException>(() => FitsDocument.Parse(file));

        Assert.Contains("END", error.Message);
    }

    [Fact]
    public void RejectsDataShorterThanDeclaredByTheHeader()
    {
        byte[] file = new FitsFileBuilder()
            .Card("SIMPLE", "T")
            .Card("BITPIX", "8")
            .Card("NAXIS", "1")
            .Card("NAXIS1", "10")
            .End()
            .Build();

        var error = Assert.Throws<FitsException>(() => FitsDocument.Parse(file));

        Assert.Contains("data", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AlignsEveryHduHeaderAndDataAreaToFITSBlocks()
    {
        using var document = FitsDocumentBuilder.Create()
            .Image([1], new byte[] { 7 })
            .Image([3], new short[] { 1, 2, 3 })
            .Build();

        byte[] serialized = document.ToArray();

        Assert.Equal(2, document.Hdus.Count);
        Assert.Equal(11520, serialized.Length);
        Assert.Equal(1, document.Hdus[0].RawData.Length);
        Assert.Equal(6, document.Hdus[1].RawData.Length);
        Assert.Equal("XTENSION", document.Hdus[1].Header.Cards[0].Keyword);
        Assert.Equal("IMAGE", document.Hdus[1].Header.GetString("XTENSION"));
        Assert.All(serialized.AsSpan(2881, 2879).ToArray(), value => Assert.Equal(0, value));
        Assert.All(serialized.AsSpan(8646, 2874).ToArray(), value => Assert.Equal(0, value));
    }

    [Fact]
    public void RejectsTrailingBytesThatDoNotCompleteAFITSBlock()
    {
        byte[] valid = new FitsFileBuilder()
            .Card("SIMPLE", "T")
            .Card("BITPIX", "8")
            .Card("NAXIS", "0")
            .Build();
        byte[] invalid = [.. valid, (byte)'\0'];

        Assert.Throws<FitsException>(() => FitsDocument.Parse(invalid));
    }

    private static string Card(string keyword, string value)
    {
        string image = keyword.PadRight(8) + value;
        return image.PadRight(80);
    }

    private static byte[] Header(params string[] cards)
    {
        byte[] header = cards
            .Concat(["END".PadRight(80)])
            .SelectMany(card => Encoding.ASCII.GetBytes(card))
            .ToArray();
        return header.Concat(Spaces(2880 - header.Length)).ToArray();
    }

    private static byte[] HeaderWithoutEnd(params string[] cards)
    {
        byte[] header = cards.SelectMany(card => Encoding.ASCII.GetBytes(card)).ToArray();
        return header.Concat(Spaces(2880 - header.Length)).ToArray();
    }

    private static IEnumerable<byte> Spaces(int count)
    {
        return Enumerable.Repeat((byte)' ', count);
    }
}
