using System.Globalization;
using System.Text;

namespace CFITS;

public sealed class AsciiTableBuilder
{
    private readonly List<AsciiColumn> _columns = [];

    public AsciiTableBuilder AddText(string name, ReadOnlySpan<string> values, int width)
    {
        var copy = values.ToArray();
        return Add(name, $"A{width}", width, copy.Length, row => Pad(copy[row] ?? "", width));
    }

    public AsciiTableBuilder AddInteger(string name, ReadOnlySpan<long> values, int width)
    {
        var copy = values.ToArray();
        return Add(name, $"I{width}", width, copy.Length, row => copy[row].ToString(CultureInfo.InvariantCulture).PadLeft(width));
    }

    public AsciiTableBuilder AddReal(string name, ReadOnlySpan<double> values, int width, int decimals, char form)
    {
        var copy = values.ToArray();
        return Add(name, $"{form}{width}.{decimals}", width, copy.Length, row => FormatReal(copy[row], width, decimals, form));
    }

    internal TablePayload Encode()
    {
        int rows = _columns.Count == 0 ? 0 : _columns[0].Rows;
        int stride = 0;
        var starts = new int[_columns.Count];
        for (int i = 0; i < _columns.Count; i++)
        {
            starts[i] = stride;
            stride += _columns[i].Width;
        }

        var data = new byte[Math.Max(stride, 0) * rows];
        Array.Fill(data, (byte)' ');
        var header = FitsHeader.Builder()
            .Text("XTENSION", "TABLE   ")
            .Integer("BITPIX", 8)
            .Integer("NAXIS", 2)
            .Integer("NAXIS1", stride)
            .Integer("NAXIS2", rows)
            .Integer("PCOUNT", 0)
            .Integer("GCOUNT", 1)
            .Integer("TFIELDS", _columns.Count);
        for (int field = 0; field < _columns.Count; field++)
        {
            var column = _columns[field];
            header.Text($"TTYPE{field + 1}", column.Name);
            header.Text($"TFORM{field + 1}", column.Format);
            header.Integer($"TBCOL{field + 1}", starts[field] + 1);
            for (int row = 0; row < rows; row++)
            {
                Encoding.ASCII.GetBytes(column.Cells[row], data.AsSpan((row * stride) + starts[field]));
            }
        }

        return new TablePayload(header.Build(), data);
    }

    private AsciiTableBuilder Add(string name, string format, int width, int rows, Func<int, string> formatRow)
    {
        var cells = new string[rows];
        for (int row = 0; row < rows; row++)
        {
            var text = formatRow(row);
            cells[row] = text.Length > width ? text[^width..] : text.PadLeft(width);
        }

        _columns.Add(new AsciiColumn(name, format, width, rows, cells));
        return this;
    }

    private static string FormatReal(double value, int width, int decimals, char form)
    {
        if (form == 'F')
        {
            return Fit(value.ToString($"F{decimals}", CultureInfo.InvariantCulture), width);
        }

        var text = value.ToString($"E{decimals}", CultureInfo.InvariantCulture);
        int mark = text.IndexOf('E');
        if (mark > 0)
        {
            var exponent = text[(mark + 1)..];
            if (exponent.Length > 3 && (exponent[0] is '+' or '-'))
            {
                exponent = exponent[0] + exponent[1..].TrimStart('0').PadLeft(2, '0');
            }

            text = text[..(mark + 1)] + exponent;
        }

        if (form == 'D')
        {
            text = text.Replace('E', 'D');
        }

        return Fit(text, width);
    }

    private static string Fit(string text, int width) => text.Length > width ? text[^width..] : text.PadLeft(width);

    private static string Pad(string text, int width) => text.Length > width ? text[..width] : text.PadRight(width);

    private sealed class AsciiColumn(string name, string format, int width, int rows, string[] cells)
    {
        public string Name { get; } = name;

        public string Format { get; } = format;

        public int Width { get; } = width;

        public int Rows { get; } = rows;

        public string[] Cells { get; } = cells;
    }
}
