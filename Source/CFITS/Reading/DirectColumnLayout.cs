using CFITS.Data;

namespace CFITS;

/// <summary>
/// Builds column objects for the names a generated model asks for.
/// Other columns contribute only their width, so their cards never become objects.
/// </summary>
internal static class DirectColumnLayout
{
    public static FitsTable Select(
        ReadOnlySpan<byte> header,
        HduShape shape,
        ReadOnlyMemory<byte> data,
        ReadOnlySpan<string> names,
        FitsScaleMode mode,
        int hduIndex,
        bool requireColumns = true)
    {
        if (names.Length == 0)
        {
            throw new FitsException("A direct table read needs at least one column.", new FitsLocation(hduIndex));
        }

        bool ascii = string.Equals(shape.Extension, "TABLE", StringComparison.OrdinalIgnoreCase);
        if (shape.Axes.Length != 2 || shape.Bitpix != 8)
        {
            throw new FitsException("Table extensions require BITPIX = 8 and NAXIS = 2.", new FitsLocation(hduIndex, "NAXIS"));
        }

        if (shape.Gcount != 1)
        {
            throw new FitsException("Table extensions require GCOUNT = 1.", new FitsLocation(hduIndex, "GCOUNT"));
        }

        int stride = shape.Axes[0];
        int rows = shape.Axes[1];
        ReadSizing(header, out long? fieldCount, out long? heapStartCard);
        int fields = checked((int)(fieldCount ?? throw new FitsException("The table header is missing TFIELDS.", new FitsLocation(hduIndex, "TFIELDS"))));
        if (fields is < 1 or > 999)
        {
            throw new FitsException("TFIELDS must be between 1 and 999.", new FitsLocation(hduIndex, "TFIELDS"));
        }

        long fixedRows = checked((long)stride * rows);
        long pcount = ascii ? 0 : shape.Pcount;
        long theap = ascii ? 0 : heapStartCard ?? fixedRows;
        long dataEnd = checked(fixedRows + pcount);
        if (!ascii && (pcount < 0 || theap < fixedRows || theap > dataEnd || dataEnd > data.Length))
        {
            throw new FitsException("THEAP and PCOUNT must identify a heap after the fixed table rows.", new FitsLocation(hduIndex, "THEAP"));
        }

        var specs = new ColumnSpec[fields];
        var sawFormat = new bool[fields];
        var namesInHeader = new string?[fields];
        var formats = new string?[fields];
        var asciiStart = new int[fields];
        Array.Fill(asciiStart, -1);
        var scale = new double[fields];
        var zero = new double[fields];
        var nulls = new long?[fields];
        var asciiNulls = new string?[fields];
        Array.Fill(scale, 1d);
        for (int offset = 0; offset + FitsBlock.CardSize <= header.Length; offset += FitsBlock.CardSize)
        {
            FitsCardView card = FitsCardView.Parse(header.Slice(offset, FitsBlock.CardSize), offset);
            if (card.IsEnd)
            {
                break;
            }

            ReadOnlySpan<byte> keyword = card.Keyword;
            if (IndexOf(keyword, "TFORM"u8, out int field))
            {
                RequireField(field, fields, hduIndex);
                string format = card.TextValue() ?? "";
                specs[field] = ColumnFormats.Parse(format, ascii);
                formats[field] = format;
                sawFormat[field] = true;
            }
            else if (IndexOf(keyword, "TTYPE"u8, out field))
            {
                RequireField(field, fields, hduIndex);
                namesInHeader[field] = card.TextValue()?.Trim();
            }
            else if (ascii && IndexOf(keyword, "TBCOL"u8, out field))
            {
                RequireField(field, fields, hduIndex);
                if (card.Kind != FitsValueKind.Integer)
                {
                    throw new FitsException($"TBCOL{field + 1} must be an integer.", new FitsLocation(hduIndex, $"TBCOL{field + 1}"));
                }

                asciiStart[field] = checked((int)card.Integer - 1);
            }
            else if (IndexOf(keyword, "TSCAL"u8, out field))
            {
                RequireField(field, fields, hduIndex);
                scale[field] = card.Kind == FitsValueKind.Integer ? card.Integer : card.Real;
            }
            else if (IndexOf(keyword, "TZERO"u8, out field))
            {
                RequireField(field, fields, hduIndex);
                zero[field] = card.Kind == FitsValueKind.Integer ? card.Integer : card.Real;
            }
            else if (IndexOf(keyword, "TNULL"u8, out field) && card.Kind == FitsValueKind.Integer)
            {
                RequireField(field, fields, hduIndex);
                nulls[field] = card.Integer;
            }
            else if (IndexOf(keyword, "TNULL"u8, out field) && ascii && card.Kind == FitsValueKind.String)
            {
                RequireField(field, fields, hduIndex);
                asciiNulls[field] = card.TextValue();
            }
        }

        int heapStart = checked((int)theap);
        int heapEnd = checked((int)dataEnd);
        var selected = new List<FitsColumn>(names.Length);
        var matched = new bool[names.Length];
        int binaryCursor = 0;
        int previousAsciiEnd = 0;
        for (int field = 0; field < fields; field++)
        {
            if (!sawFormat[field])
            {
                throw new FitsException($"The table header is missing TFORM{field + 1}.", new FitsLocation(hduIndex, $"TFORM{field + 1}"));
            }

            ColumnSpec spec = specs[field];
            int columnOffset = binaryCursor;
            if (ascii)
            {
                columnOffset = asciiStart[field];
                if (columnOffset < 0 || spec.Width > stride - columnOffset)
                {
                    throw new FitsException($"ASCII column {field + 1} extends beyond NAXIS1.", new FitsLocation(hduIndex, $"TBCOL{field + 1}"));
                }

                if (columnOffset < previousAsciiEnd)
                {
                    throw new FitsException($"ASCII column {field + 1} overlaps a previous column.", new FitsLocation(hduIndex, $"TBCOL{field + 1}"));
                }

                previousAsciiEnd = columnOffset + spec.Width;
            }
            else
            {
                binaryCursor = checked(binaryCursor + spec.Width);
            }

            if (scale[field] == 0)
            {
                throw new FitsException($"TSCAL{field + 1} must not be zero.", new FitsLocation(hduIndex, $"TSCAL{field + 1}"));
            }

            if (!ascii && spec.Code == 'L' && (scale[field] != 1d || zero[field] != 0d))
            {
                throw new FitsException($"Scaling keywords are not valid for logical column {field + 1}.", new FitsLocation(hduIndex, $"TSCAL{field + 1}"));
            }

            int wanted = Wanted(names, namesInHeader[field]);
            if (wanted < 0 || matched[wanted])
            {
                continue;
            }

            matched[wanted] = true;

            if (nulls[field] is long marker && !ascii)
            {
                ValidateNull(marker, spec.Code, field + 1, hduIndex);
            }

            selected.Add(new FitsColumn(
                namesInHeader[field] ?? names[wanted],
                formats[field] ?? "",
                spec,
                columnOffset,
                stride,
                heapStart,
                heapEnd,
                scale[field],
                zero[field],
                nulls[field],
                asciiNulls[field],
                dimensions: null,
                minimum: null,
                maximum: null,
                data,
                mode,
                rows,
                hduIndex));
        }

        if (!ascii && binaryCursor > stride)
        {
            throw new FitsException("Binary-table columns are wider than NAXIS1.", new FitsLocation(hduIndex));
        }

        for (int index = 0; index < names.Length; index++)
        {
            if (!matched[index] && requireColumns)
            {
                throw new FitsException($"Column '{names[index]}' was not found.", new FitsLocation(hduIndex, Column: names[index]));
            }
        }

        foreach (FitsColumn column in selected)
        {
            column.ValidateVariableArrays();
        }

        return FitsTable.FromColumns(ascii, rows, selected);
    }

    private static void ReadSizing(ReadOnlySpan<byte> header, out long? fields, out long? heap)
    {
        fields = null;
        heap = null;
        for (int offset = 0; offset + FitsBlock.CardSize <= header.Length; offset += FitsBlock.CardSize)
        {
            FitsCardView card = FitsCardView.Parse(header.Slice(offset, FitsBlock.CardSize), offset);
            if (card.IsEnd)
            {
                return;
            }

            if (card.Kind != FitsValueKind.Integer)
            {
                continue;
            }

            if (card.Keyword.SequenceEqual("TFIELDS"u8))
            {
                fields = card.Integer;
            }
            else if (card.Keyword.SequenceEqual("THEAP"u8))
            {
                heap = card.Integer;
            }
        }
    }

    private static void RequireField(int field, int fields, int hduIndex)
    {
        if ((uint)field >= (uint)fields)
        {
            throw new FitsException("A table keyword index is outside TFIELDS.", new FitsLocation(hduIndex, "TFIELDS"));
        }
    }

    private static int Wanted(ReadOnlySpan<string> names, string? candidate)
    {
        if (candidate is null)
        {
            return -1;
        }

        for (int index = 0; index < names.Length; index++)
        {
            if (string.Equals(names[index], candidate, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IndexOf(ReadOnlySpan<byte> keyword, ReadOnlySpan<byte> prefix, out int field)
    {
        field = -1;
        if (!keyword.StartsWith(prefix) || keyword.Length == prefix.Length)
        {
            return false;
        }

        int number = 0;
        for (int index = prefix.Length; index < keyword.Length; index++)
        {
            byte digit = keyword[index];
            if (digit is < (byte)'0' or > (byte)'9')
            {
                return false;
            }

            number = (number * 10) + (digit - (byte)'0');
        }

        if (number is < 1 or > 999)
        {
            return false;
        }

        field = number - 1;
        return true;
    }

    private static void ValidateNull(long value, char code, int field, int hduIndex)
    {
        (long minimum, long maximum) = code switch
        {
            'B' => (byte.MinValue, byte.MaxValue),
            'I' => (short.MinValue, short.MaxValue),
            'J' => (int.MinValue, int.MaxValue),
            'K' => (long.MinValue, long.MaxValue),
            _ => throw new FitsException($"TNULL{field} is only valid for binary integer columns.", new FitsLocation(hduIndex, $"TNULL{field}")),
        };
        if (value < minimum || value > maximum)
        {
            throw new FitsException($"TNULL{field} is outside the stored column domain.", new FitsLocation(hduIndex, $"TNULL{field}"));
        }
    }
}
