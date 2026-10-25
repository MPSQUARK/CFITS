using System.Collections.Frozen;

namespace CFITS;

/// <summary>An ASCII or binary table. Cells are addressed by row and element.</summary>
public sealed class FitsTable
{
    private FitsTable(bool ascii, int rows, IReadOnlyList<FitsColumn> columns)
    {
        IsAscii = ascii;
        RowCount = rows;
        Columns = columns;
        var lookup = new Dictionary<string, FitsColumn>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in columns)
        {
            lookup.TryAdd(column.Name, column);
        }

        _lookup = lookup.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    private readonly FrozenDictionary<string, FitsColumn> _lookup;

    public bool IsAscii { get; }

    public int RowCount { get; }

    public IReadOnlyList<FitsColumn> Columns { get; }

    public FitsColumn Column(string name)
    {
        if (name is null)
        {
            throw new FitsException("Column name cannot be null.", new FitsLocation());
        }

        if (_lookup.TryGetValue(name, out FitsColumn? column))
        {
            return column;
        }

        throw new FitsException($"Column '{name}' was not found.", new FitsLocation(Column: name));
    }

    internal static FitsTable Create(FitsHeader header, ReadOnlyMemory<byte> data, bool ascii, HduShape shape, FitsScaleMode mode)
    {
        int fields = checked((int)header.GetInt64("TFIELDS"));
        if (fields < 1 || fields > 999)
        {
            throw new FitsException("TFIELDS must be between 1 and 999.", new FitsLocation(header.HduIndex, "TFIELDS"));
        }

        int rows = shape.Axes.Length > 1 ? shape.Axes[1] : 0;
        int stride = shape.Axes.Length > 0 ? shape.Axes[0] : 0;
        long fixedRows = checked((long)stride * rows);
        long pcount = ascii ? 0 : header.GetInt64("PCOUNT");
        long theap = ascii
            ? 0
            : header.TryGetCard("THEAP", out var heapCard) ? heapCard.GetInt64() : fixedRows;
        bool dataEndOverflow = pcount > 0 && fixedRows > long.MaxValue - pcount;
        long dataEnd = dataEndOverflow ? long.MaxValue : fixedRows + pcount;
        if (!ascii && (pcount < 0 || dataEndOverflow || theap < fixedRows || theap > dataEnd ||
            dataEnd > data.Length))
        {
            throw new FitsException("THEAP and PCOUNT must identify a heap after the fixed table rows.", new FitsLocation(header.HduIndex, "THEAP"));
        }
        int heapStart = checked((int)theap);
        int heapEnd = checked((int)(fixedRows + pcount));
        var columns = new List<FitsColumn>(fields);
        int binaryCursor = 0;
        int previousAsciiEnd = 0;
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
                if (asciiStart < previousAsciiEnd)
                {
                    throw new FitsException($"ASCII column {field} overlaps a previous column.", new FitsLocation(header.HduIndex, $"TBCOL{field}"));
                }

                offset = asciiStart;
                previousAsciiEnd = asciiStart + spec.Width;
            }
            else
            {
                binaryCursor = checked(binaryCursor + spec.Width);
            }

            double scale = header.TryGetCard($"TSCAL{field}", out var scaleCard) ? scaleCard.GetDouble() : 1;
            double zero = header.TryGetCard($"TZERO{field}", out var zeroCard) ? zeroCard.GetDouble() : 0;
            if (scale == 0)
            {
                throw new FitsException($"TSCAL{field} must not be zero.", new FitsLocation(header.HduIndex, $"TSCAL{field}"));
            }
            if (!ascii && spec.Code == 'L' && (header.TryGetCard($"TSCAL{field}", out _) || header.TryGetCard($"TZERO{field}", out _)))
            {
                throw new FitsException($"Scaling keywords are not valid for logical column {field}.", new FitsLocation(header.HduIndex, $"TSCAL{field}"));
            }

            long? nullValue = null;
            string? asciiNull = null;
            if (header.TryGetCard($"TNULL{field}", out var nullCard))
            {
                if (!ascii && (spec.Code is not ('B' or 'I' or 'J' or 'K') || nullCard.Kind != FitsValueKind.Integer))
                {
                    throw new FitsException($"TNULL{field} is only valid for binary integer columns.", new FitsLocation(header.HduIndex, $"TNULL{field}"));
                }
                if (nullCard.Kind == FitsValueKind.Integer)
                {
                    nullValue = nullCard.Integer;
                    if (!ascii)
                    {
                        ValidateNullRange(nullValue.Value, spec.Code, header, field);
                    }
                }
                else
                {
                    asciiNull = nullCard.GetString();
                }
            }

            int[]? dimensions = null;
            if (header.TryGetCard($"TDIM{field}", out var dimCard))
            {
                if (ascii)
                {
                    throw new FitsException($"TDIM{field} is not valid for an ASCII table.", new FitsLocation(header.HduIndex, $"TDIM{field}"));
                }
                dimensions = ColumnFormats.ParseDimensions(dimCard.GetString() ?? "");
                ColumnFormats.ValidateDimensions(dimensions, spec);
            }
            (double? minimum, double? maximum) = ReadLimits(header, field, spec);

            columns.Add(new FitsColumn(
                name,
                format,
                spec,
                offset,
                stride,
                heapStart,
                heapEnd,
                scale,
                zero,
                nullValue,
                asciiNull,
                dimensions,
                minimum,
                maximum,
                data,
                mode,
                rows,
                header.HduIndex));
        }

        if (!ascii && binaryCursor > stride)
        {
            throw new FitsException("Binary-table columns are wider than NAXIS1.", new FitsLocation(header.HduIndex));
        }

        return new FitsTable(ascii, rows, columns);
    }

    private static void ValidateNullRange(long value, char code, FitsHeader header, int field)
    {
        long minimum = code switch
        {
            'B' => byte.MinValue,
            'I' => short.MinValue,
            'J' => int.MinValue,
            'K' => long.MinValue,
            _ => throw new InvalidOperationException(),
        };
        long maximum = code switch
        {
            'B' => byte.MaxValue,
            'I' => short.MaxValue,
            'J' => int.MaxValue,
            'K' => long.MaxValue,
            _ => throw new InvalidOperationException(),
        };
        if (value < minimum || value > maximum)
        {
            throw new FitsException($"TNULL{field} is outside the stored column domain.", new FitsLocation(header.HduIndex, $"TNULL{field}"));
        }
    }

    private static (double? Minimum, double? Maximum) ReadLimits(FitsHeader header, int field, ColumnSpec spec)
    {
        bool vector = spec.Variable || spec.Repeat > 1;
        string minimumKeyword = $"{(vector ? "TDMIN" : "TLMIN")}{field}";
        string maximumKeyword = $"{(vector ? "TDMAX" : "TLMAX")}{field}";
        string wrongMinimumKeyword = $"{(vector ? "TLMIN" : "TDMIN")}{field}";
        string wrongMaximumKeyword = $"{(vector ? "TLMAX" : "TDMAX")}{field}";
        if (header.TryGetCard(wrongMinimumKeyword, out _) || header.TryGetCard(wrongMaximumKeyword, out _))
        {
            throw new FitsException($"{wrongMinimumKeyword} and {wrongMaximumKeyword} do not match the column domain.", new FitsLocation(header.HduIndex, wrongMinimumKeyword));
        }

        if (!IsLimitDomain(spec))
        {
            if (header.TryGetCard(minimumKeyword, out _) || header.TryGetCard(maximumKeyword, out _))
            {
                throw new FitsException($"{minimumKeyword} and {maximumKeyword} are not valid for this column type.", new FitsLocation(header.HduIndex, minimumKeyword));
            }

            return (null, null);
        }

        double? minimum = ReadLimit(header, minimumKeyword);
        double? maximum = ReadLimit(header, maximumKeyword);
        if (minimum is double lower && maximum is double upper && lower > upper)
        {
            throw new FitsException($"{minimumKeyword} must not exceed {maximumKeyword}.", new FitsLocation(header.HduIndex, minimumKeyword));
        }

        return (minimum, maximum);
    }

    private static double? ReadLimit(FitsHeader header, string keyword)
    {
        if (!header.TryGetCard(keyword, out var card))
        {
            return null;
        }

        if (card.Kind is not (FitsValueKind.Integer or FitsValueKind.Real))
        {
            throw new FitsException($"{keyword} must be numeric.", new FitsLocation(header.HduIndex, keyword));
        }

        return card.GetDouble();
    }

    private static bool IsLimitDomain(ColumnSpec spec)
    {
        char code = spec.Variable ? spec.ElementCode : spec.Code;
        return code is 'B' or 'I' or 'J' or 'K' or 'E' or 'D'
            || (spec.Ascii && code is ('I' or 'F' or 'E' or 'D'));
    }
}
