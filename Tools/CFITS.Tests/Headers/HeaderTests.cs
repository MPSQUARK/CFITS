using System.Text;

namespace CFITS.Tests;

public sealed class HeaderTests
{
    [Fact]
    public void ParsesTheStandardValueForms()
    {
        var file = new FitsFileBuilder()
            .Card("SIMPLE", "T")
            .Card("BITPIX", "16")
            .Card("NAXIS", "0")
            .Card("INT", "-42")
            .Card("REAL", "1.5")
            .Card("DEXP", "1.25D-08")
            .Text("STR", "O'HARA")
            .Text("EMPTY", "")
            .Card("UNDEF", "")
            .Comment("COMMENT", "hello")
            .Build();
        using var document = FitsDocument.Parse(file);
        var header = document.Hdus[0].Header;
        Assert.Equal(-42, header.GetInt64("INT"));
        Assert.Equal(1.5, header.GetDouble("REAL"));
        Assert.Equal(1.25e-8, header.GetDouble("DEXP"));
        Assert.Equal("O'HARA", header.GetString("STR"));
        Assert.Equal("", header.GetString("EMPTY"));
        Assert.False(header.TryGetCard("UNDEF", out var undefined) && undefined.Kind != FitsValueKind.Undefined);
        Assert.Contains(header.Cards, card => card.Keyword == "COMMENT" && card.Text == "hello");
    }

    [Fact]
    public void RejectsAMissingEndRecord()
    {
        var bytes = Encoding.ASCII.GetBytes("SIMPLE  =                    T" + new string(' ', 52));
        var error = Assert.Throws<FitsException>(() => FitsDocument.Parse(bytes));
        Assert.Contains("END", error.Message);
    }

    [Fact]
    public void RejectsNonAsciiHeaderBytes()
    {
        byte[] bytes = Enumerable.Repeat((byte)' ', 2880).ToArray();
        bytes[0] = (byte)'S';
        bytes[1] = (byte)'I';
        bytes[2] = (byte)'M';
        bytes[3] = (byte)'P';
        bytes[4] = (byte)'L';
        bytes[5] = (byte)'E';
        bytes[8] = (byte)'=';
        bytes[9] = (byte)' ';
        bytes[10] = 0x80;

        var error = Assert.Throws<FitsException>(() => FitsDocument.Parse(bytes));

        Assert.Contains("ASCII", error.Message);
    }

    [Fact]
    public void RejectsNonAsciiTextWhenWritingHeader()
    {
        var error = Assert.Throws<FitsException>(() => FitsDocumentBuilder.Create()
            .Image(Array.Empty<int>(), Array.Empty<byte>(), header => header.Text("TEXT", "café"))
            .Build());

        Assert.Contains("ASCII", error.Message);
    }

    [Fact]
    public void RejectsAnIllegalBitpix()
    {
        var file = new FitsFileBuilder()
            .Card("SIMPLE", "T")
            .Card("BITPIX", "12")
            .Card("NAXIS", "0")
            .End()
            .Build();
        var error = Assert.Throws<FitsException>(() => FitsDocument.Parse(file));
        Assert.Contains("BITPIX", error.Message);
    }

    [Fact]
    public void RejectsATruncatedDataArray()
    {
        var file = new FitsFileBuilder()
            .Card("SIMPLE", "T")
            .Card("BITPIX", "16")
            .Card("NAXIS", "1")
            .Card("NAXIS1", "10")
            .Build();
        var error = Assert.Throws<FitsException>(() => FitsDocument.Parse(file));
        Assert.Contains("data", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, error.HduIndex);
    }

    [Fact]
    public void SerializesLongHeaderCardsWithoutChangingThem()
    {
        string text = new('x', 100);
        using var document = FitsDocumentBuilder.Create()
            .Image(Array.Empty<int>(), Array.Empty<byte>(), header => header.Text("LONGSTR", text))
            .Build();

        byte[] first = document.ToArray();
        using var reparsed = FitsDocument.Parse(first);

        Assert.Equal(text, reparsed.Hdus[0].Header.GetString("LONGSTR"));
        Assert.Equal(first, reparsed.ToArray());
    }

    [Fact]
    public void RejectsNegativeAxisLength()
    {
        var file = new FitsFileBuilder()
            .Card("SIMPLE", "T")
            .Card("BITPIX", "16")
            .Card("NAXIS", "1")
            .Card("NAXIS1", "-1")
            .End()
            .Build();

        var error = Assert.Throws<FitsException>(() => FitsDocument.Parse(file));
        Assert.Contains("NAXIS1", error.Message);
    }

    [Theory]
    [InlineData("9223372036854775807", long.MaxValue)]
    [InlineData("-9223372036854775808", long.MinValue)]
    [InlineData("+0", 0)]
    public void ParsesIntegerBoundariesWithoutChangingValues(string text, long expected)
    {
        var file = new FitsFileBuilder()
            .Card("SIMPLE", "T")
            .Card("BITPIX", "8")
            .Card("NAXIS", "0")
            .Card("VALUE", text)
            .End()
            .Build();

        using var document = FitsDocument.Parse(file);

        Assert.Equal(expected, document.Hdus[0].Header.GetInt64("VALUE"));
    }

    [Fact]
    public void RejectsAnIntegerOutsideThe64BitRange()
    {
        var file = new FitsFileBuilder()
            .Card("SIMPLE", "T")
            .Card("BITPIX", "8")
            .Card("NAXIS", "0")
            .Card("VALUE", "9223372036854775808")
            .End()
            .Build();

        var error = Assert.Throws<FitsException>(() => FitsDocument.Parse(file));

        Assert.Contains("64-bit", error.Message);
    }
}
