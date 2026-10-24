using System.Text;

namespace CFITS.Tests;

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
