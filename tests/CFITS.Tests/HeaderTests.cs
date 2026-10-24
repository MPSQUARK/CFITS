using System.Text;

namespace CFITS.Tests;

public class HeaderTests
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
}

internal sealed class FitsFileBuilder
{
    private readonly List<byte> _bytes = [];

    public FitsFileBuilder Card(string keyword, string value)
    {
        var text = keyword.PadRight(8) + "= " + value.PadLeft(20);
        _bytes.AddRange(Encoding.ASCII.GetBytes(text.PadRight(80)));
        return this;
    }

    public FitsFileBuilder Text(string keyword, string value)
    {
        var encoded = value.Replace("'", "''");
        var text = keyword.PadRight(8) + "= '" + encoded + "'";
        _bytes.AddRange(Encoding.ASCII.GetBytes(text.PadRight(80)));
        return this;
    }

    public FitsFileBuilder Comment(string keyword, string value)
    {
        var text = keyword.PadRight(8) + value;
        _bytes.AddRange(Encoding.ASCII.GetBytes(text.PadRight(80)));
        return this;
    }

    public FitsFileBuilder End()
    {
        _bytes.AddRange(Encoding.ASCII.GetBytes("END".PadRight(80)));
        return this;
    }

    public byte[] Build()
    {
        End();
        while (_bytes.Count % 2880 != 0)
        {
            _bytes.Add((byte)' ');
        }

        return _bytes.ToArray();
    }
}
