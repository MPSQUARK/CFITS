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

    [Theory]
    [InlineData("BAD.KEY")]
    [InlineData("badkey")]
    [InlineData("BAD KEY")]
    public void RejectsInvalidKeywordNames(string keyword)
    {
        byte[] file = Header(
            Card("SIMPLE", "= T"),
            Card("BITPIX", "= 8"),
            Card("NAXIS", "= 0"),
            Card(keyword, "= 1"));

        var error = Assert.Throws<FitsException>(() => FitsDocument.Parse(file));

        Assert.Contains("keyword", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("=T")]
    [InlineData("= 1trailing")]
    [InlineData("= 1/without-space")]
    public void RejectsInvalidCardDelimiters(string value)
    {
        byte[] file = Header(
            Card("SIMPLE", "= T"),
            Card("BITPIX", "= 8"),
            Card("NAXIS", "= 0"),
            Card("VALUE", value));

        Assert.Throws<FitsException>(() => FitsDocument.Parse(file));
    }

    [Fact]
    public void PreservesLongStringBoundaryQuotesAndCommentOnWrite()
    {
        string text = new string('x', 66) + "'" + new string('y', 5);
        using var document = FitsDocumentBuilder.Create()
            .Image(Array.Empty<int>(), Array.Empty<byte>(), header => header.Text("LONGSTR", text, "kept"))
            .Build();

        byte[] bytes = document.ToArray();
        using var reparsed = FitsDocument.Parse(bytes);

        Assert.Equal(text, reparsed.Hdus[0].Header.GetString("LONGSTR"));
        Assert.Contains(reparsed.Hdus[0].Header.Cards, card =>
            card.Keyword == "LONGSTR" && card.Comment == "kept");
        Assert.Equal(bytes, reparsed.ToArray());
    }

    [Fact]
    public void PreservesSignedZeroAndRejectsIntegerOverflow()
    {
        byte[] file = Header(
            Card("SIMPLE", "= T"),
            Card("BITPIX", "= 8"),
            Card("NAXIS", "= 0"),
            Card("ZERO", "= -0.0"),
            Card("TOO_BIG", "= 9223372036854775808"));

        var error = Assert.Throws<FitsException>(() => FitsDocument.Parse(file));
        Assert.Contains("range", error.Message, StringComparison.OrdinalIgnoreCase);

        using var valid = FitsDocument.Parse(Header(
            Card("SIMPLE", "= T"),
            Card("BITPIX", "= 8"),
            Card("NAXIS", "= 0"),
            Card("ZERO", "= -0.0"),
            Card("DATE-OBS", "= '2026-10-25T21:00:00'"),
            Card("UNDEF", "= "),
            Card("COMPLEX", "= (1.0D+0, -2.0D+0)")));
        Assert.True(double.IsNegative(valid.Hdus[0].Header.GetDouble("ZERO")));
        Assert.Equal("2026-10-25T21:00:00", valid.Hdus[0].Header.GetString("DATE-OBS"));
        Assert.Equal(new FitsComplex(1, -2), valid.Hdus[0].Header.TryGetCard("COMPLEX", out var complex)
            ? complex.GetComplex()
            : throw new Xunit.Sdk.XunitException("Missing complex card"));
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
    public void PreservesExactKeywordsCommentsAndContinuationSemantics()
    {
        byte[] file = Header(
            Card("SIMPLE", "= T"),
            Card("BITPIX", "= 8"),
            Card("NAXIS", "= 0"),
            Card("VALUE", "= 'first&' / first comment"),
            Card("CONTINUE", "  'second' / second comment"),
            Card("COMMENT", "commentary"),
            Card("HISTORY", "history"),
            Card("", "blank commentary"));

        using var document = FitsDocument.Parse(file);
        FitsCard[] cards = document.Hdus[0].Header.Cards.ToArray();

        Assert.Collection(
            cards,
            card => Assert.Equal(new FitsCard("SIMPLE", FitsValueKind.Logical, true, 0, 0, 0, null, "", null), card),
            card => Assert.Equal(new FitsCard("BITPIX", FitsValueKind.Integer, false, 8, 8, 0, null, "", null), card),
            card => Assert.Equal(new FitsCard("NAXIS", FitsValueKind.Integer, false, 0, 0, 0, null, "", null), card),
            card => Assert.Equal(new FitsCard("VALUE", FitsValueKind.String, false, 0, 0, 0, "firstsecond", "first comment second comment", null), card),
            card => Assert.Equal(new FitsCard("COMMENT", FitsValueKind.Commentary, false, 0, 0, 0, "commentary", "", null), card),
            card => Assert.Equal(new FitsCard("HISTORY", FitsValueKind.Commentary, false, 0, 0, 0, "history", "", null), card),
            card => Assert.Equal(new FitsCard("", FitsValueKind.Commentary, false, 0, 0, 0, "blank commentary", "", null), card));
        Assert.Equal(file, document.ToArray());
    }

    [Fact]
    public void ConvertsAnOrphanContinueToExactCommentary()
    {
        byte[] file = Header(
            Card("SIMPLE", "= T"),
            Card("BITPIX", "= 8"),
            Card("NAXIS", "= 0"),
            Card("CONTINUE", "  'orphan' / retained"));

        using var document = FitsDocument.Parse(file);

        Assert.Contains(document.Hdus[0].Header.Cards,
            card => card == new FitsCard("COMMENT", FitsValueKind.Commentary, false, 0, 0, 0, "  'orphan' / retained", "", null));
        Assert.Equal(file, document.ToArray());
    }

    [Theory]
    [InlineData("  no opening quote")]
    [InlineData("  'unterminated")]
    [InlineData("  'value'/missing delimiter space")]
    [InlineData("= 'not a continuation value'")]
    public void RejectsMalformedContinueCards(string value)
    {
        byte[] file = Header(
            Card("SIMPLE", "= T"),
            Card("BITPIX", "= 8"),
            Card("NAXIS", "= 0"),
            Card("CONTINUE", value));

        Assert.Throws<FitsException>(() => FitsDocument.Parse(file));
    }

    [Fact]
    public void TreatsAnOrphanContinueCardAsCommentary()
    {
        byte[] file = Header(
            Card("SIMPLE", "= T"),
            Card("BITPIX", "= 8"),
            Card("NAXIS", "= 0"),
            Card("CONTINUE", "  'fragment'"));

        using var document = FitsDocument.Parse(file);

        Assert.Contains(document.Hdus[0].Header.Cards,
            card => card is { Keyword: "COMMENT", Kind: FitsValueKind.Commentary, Text: "  'fragment'" });
    }

    [Fact]
    public void TreatsAnAmpersandWithoutAFollowingContinueAsLiteral()
    {
        byte[] file = Header(
            Card("SIMPLE", "= T"),
            Card("BITPIX", "= 8"),
            Card("NAXIS", "= 0"),
            Card("LONGSTR", "= 'fragment&'"));

        using var document = FitsDocument.Parse(file);

        Assert.Equal("fragment&", document.Hdus[0].Header.GetString("LONGSTR"));
    }

    [Fact]
    public void ReservesHeaderSpaceThenReplacesCardsWithoutMovingData()
    {
        using var document = FitsDocumentBuilder.Create()
            .Image([1], new byte[] { 7 }, header => header
                .Reserve(40)
                .ReplaceText(0, "ORIGIN", "SDSS")
                .ReplaceInteger(1, "VERSION", 4)
            )
            .Build();

        byte[] bytes = document.ToArray();

        Assert.Equal(7, bytes[5760]);
        Assert.Equal("SDSS", document.Hdus[0].Header.GetString("ORIGIN"));
        Assert.Equal(4, document.Hdus[0].Header.GetInt64("VERSION"));
        Assert.Equal(2, document.Hdus[0].Header.Cards.Count(card => card.Keyword is "ORIGIN" or "VERSION"));
    }

    [Fact]
    public void RejectsAReservedReplacementThatNeedsMultiplePhysicalCards()
    {
        var error = Assert.Throws<FitsException>(() =>
            FitsDocumentBuilder.Create()
                .Image([1], new byte[] { 7 }, header => header
                    .Reserve(1)
                    .ReplaceText(0, "LONGSTR", new string('x', 100)))
                .Build());

        Assert.Contains("reserved", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(17)]
    [InlineData(18)]
    [InlineData(19)]
    [InlineData(65)]
    [InlineData(66)]
    [InlineData(67)]
    [InlineData(68)]
    [InlineData(69)]
    [InlineData(131)]
    public void WritesAndReadsLongStringsAtEveryContinuationBoundary(int quotePosition)
    {
        string text = new string('x', quotePosition) + "'" + new string('y', 140);

        using var document = FitsDocumentBuilder.Create()
            .Image(Array.Empty<int>(), Array.Empty<byte>(), header => header.Text("LONGSTR", text, "kept"))
            .Build();

        byte[] bytes = document.ToArray();
        using var reparsed = FitsDocument.Parse(bytes);

        Assert.Equal(text, reparsed.Hdus[0].Header.GetString("LONGSTR"));
        Assert.Equal("kept", reparsed.Hdus[0].Header.TryGetCard("LONGSTR", out var card)
            ? card.Comment
            : null);
        Assert.Equal(bytes, reparsed.ToArray());
    }

    [Fact]
    public void WritesEmptyAndCommentHeavyLongStringsWithStrictContinueSyntax()
    {
        const string text = "short";
        using var document = FitsDocumentBuilder.Create()
            .Image(Array.Empty<int>(), Array.Empty<byte>(), header => header.Text("LONGSTR", text, new string('c', 47)))
            .Build();

        byte[] bytes = document.ToArray();
        using var reparsed = FitsDocument.Parse(bytes);

        Assert.Equal(text, reparsed.Hdus[0].Header.GetString("LONGSTR"));
        Assert.Equal(new string('c', 47), reparsed.Hdus[0].Header.GetString("LONGSTR") is not null
            ? reparsed.Hdus[0].Header.Cards.Single(card => card.Keyword == "LONGSTR").Comment
            : null);
        Assert.Equal(bytes, reparsed.ToArray());
    }

    [Fact]
    public void WritesAnEmptyLongStringWithAnOversizedCommentAsARejectedCard()
    {
        var error = Assert.Throws<FitsException>(() =>
            FitsDocumentBuilder.Create()
                .Image(Array.Empty<int>(), Array.Empty<byte>(), header =>
                    header.Text("LONGSTR", "", new string('c', 48)))
                .Build());

        Assert.Contains("comment", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PreservesContinuationCommentsAndPhysicalCardGeometry()
    {
        byte[] file = Header(
            Card("SIMPLE", "= T"),
            Card("BITPIX", "= 8"),
            Card("NAXIS", "= 0"),
            Card("LONGSTR", "= 'first&' / first"),
            Card("CONTINUE", "  'second&' / second"),
            Card("CONTINUE", "  'third' / final"));

        using var document = FitsDocument.Parse(file);
        byte[] roundTrip = document.ToArray();

        Assert.Equal(file, roundTrip);
        Assert.Equal("firstsecondthird", document.Hdus[0].Header.GetString("LONGSTR"));
        Assert.Equal("first second final", document.Hdus[0].Header.Cards.Single(card => card.Keyword == "LONGSTR").Comment);
        Assert.Equal(80, roundTrip.Length >= 320 ? 80 : 0);
        Assert.Equal("CONTINUE", Encoding.ASCII.GetString(roundTrip, 320, 8).Trim());
        Assert.Equal("CONTINUE", Encoding.ASCII.GetString(roundTrip, 400, 8).Trim());
    }

    [Fact]
    public void PreservesLongStringValuesAndCardOrderingWhenCombiningContinuations()
    {
        byte[] file = Header(
            Card("SIMPLE", "= T"),
            Card("BITPIX", "= 8"),
            Card("NAXIS", "= 0"),
            Card("BEFORE", "= 1"),
            Card("LONGSTR", "= 'first&' / first"),
            Card("CONTINUE", "  'second&' / second"),
            Card("CONTINUE", "  'third' / final"),
            Card("AFTER", "= 2"),
            Card("COMMENT", "kept"));

        using var document = FitsDocument.Parse(file);
        var cards = document.Hdus[0].Header.Cards;

        Assert.Equal(
            ["SIMPLE", "BITPIX", "NAXIS", "BEFORE", "LONGSTR", "AFTER", "COMMENT"],
            cards.Select(card => card.Keyword));
        Assert.Equal("firstsecondthird", cards[4].Text);
        Assert.Equal("first second final", cards[4].Comment);
        Assert.Equal(file, document.ToArray());
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
