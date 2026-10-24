using System.Buffers.Binary;
using System.Buffers.Text;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace CFITS;

/// <summary>An ASCII or binary table. Cells are addressed by row and element.</summary>
public sealed class FitsTable
{
    private FitsTable(bool ascii, int rows, IReadOnlyList<FitsColumn> columns)
    {
        IsAscii = ascii;
        RowCount = rows;
        Columns = columns;
    }

    public bool IsAscii { get; }

    public int RowCount { get; }

    public IReadOnlyList<FitsColumn> Columns { get; }

    public FitsColumn Column(string name)
    {
        foreach (var column in Columns)
        {
            if (column.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return column;
            }
        }

        throw new FitsException($"Column '{name}' was not found.", new FitsLocation(Column: name));
    }

    internal static FitsTable Create(FitsHeader header, ReadOnlyMemory<byte> data, bool ascii, HduShape shape, FitsScaleMode mode)
    {
        int fields = checked((int)header.GetInt64("TFIELDS"));
        int rows = shape.Axes.Length > 1 ? shape.Axes[1] : 0;
        int stride = shape.Axes.Length > 0 ? shape.Axes[0] : 0;
        int theap = ascii ? 0 : header.TryGetCard("THEAP", out var heapCard) ? checked((int)heapCard.GetInt64()) : checked(stride * rows);
        if (theap < 0 || theap > data.Length)
        {
            throw new FitsException("THEAP must identify a position within the table data.", new FitsLocation(header.HduIndex, "THEAP"));
        }
        var columns = new List<FitsColumn>(fields);
        int binaryCursor = 0;
        for (int field = 1; field <= fields; field++)
        {
            string format = header.GetString($"TFORM{field}").Trim();
            string name = header.TryGetCard($"TTYPE{field}", out var typeCard) ? typeCard.GetString() ?? $"Column{field}" : $"Column{field}";
            var spec = ColumnFormats.Parse(format, ascii);
            int offset = binaryCursor;
            int asciiStart = 0;
            if (ascii)
            {
                long columnStart = header.GetInt64($"TBCOL{field}");
                if (columnStart < 1 || columnStart > int.MaxValue)
                {
                    throw new FitsException($"TBCOL{field} must identify a positive column position.", new FitsLocation(header.HduIndex, $"TBCOL{field}"));
                }

                asciiStart = (int)columnStart - 1;
                if (asciiStart > stride || spec.Width > stride - asciiStart)
                {
                    throw new FitsException($"ASCII column {field} extends beyond NAXIS1.", new FitsLocation(header.HduIndex, $"TBCOL{field}"));
                }

                offset = asciiStart;
            }
            else
            {
                binaryCursor = checked(binaryCursor + spec.Width);
            }

            double scale = header.TryGetCard($"TSCAL{field}", out var scaleCard) ? scaleCard.GetDouble() : 1;
            double zero = header.TryGetCard($"TZERO{field}", out var zeroCard) ? zeroCard.GetDouble() : 0;
            long? nullValue = null;
            string? asciiNull = null;
            if (header.TryGetCard($"TNULL{field}", out var nullCard))
            {
                if (nullCard.Kind == FitsValueKind.Integer)
                {
                    nullValue = nullCard.Integer;
                }
                else
                {
                    asciiNull = nullCard.GetString();
                }
            }

            int[]? dimensions = null;
            if (header.TryGetCard($"TDIM{field}", out var dimCard))
            {
                dimensions = ColumnFormats.ParseDimensions(dimCard.GetString() ?? "");
                ColumnFormats.ValidateDimensions(dimensions, spec);
            }

            columns.Add(new FitsColumn(
                name,
                format,
                spec,
                offset,
                stride,
                theap,
                scale,
                zero,
                nullValue,
                asciiNull,
                dimensions,
                data,
                mode,
                rows,
                header.HduIndex));
        }

        if (!ascii && binaryCursor > stride && rows > 0)
        {
            throw new FitsException("Binary-table columns are wider than NAXIS1.", new FitsLocation(header.HduIndex));
        }

        return new FitsTable(ascii, rows, columns);
    }
}
