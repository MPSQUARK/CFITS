using System.Buffers.Binary;
using System.Globalization;
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
        int theap = ascii ? 0 : header.TryGetCard("THEAP", out var heapCard) ? checked((int)heapCard.GetInt64()) : stride * rows;
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
                asciiStart = checked((int)header.GetInt64($"TBCOL{field}")) - 1;
                offset = asciiStart;
            }
            else
            {
                binaryCursor += spec.Width;
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
                header.HduIndex));
        }

        if (!ascii && binaryCursor > stride && rows > 0)
        {
            throw new FitsException("Binary-table columns are wider than NAXIS1.", new FitsLocation(header.HduIndex));
        }

        return new FitsTable(ascii, rows, columns);
    }
}

public sealed class FitsColumn
{
    private readonly ColumnSpec _spec;
    private readonly int _offset;
    private readonly int _stride;
    private readonly int _heap;
    private readonly double _scale;
    private readonly double _zero;
    private readonly long? _null;
    private readonly string? _asciiNull;
    private readonly ReadOnlyMemory<byte> _data;
    private readonly FitsScaleMode _mode;
    private readonly int _hduIndex;

    internal FitsColumn(
        string name,
        string format,
        ColumnSpec spec,
        int offset,
        int stride,
        int heap,
        double scale,
        double zero,
        long? nullValue,
        string? asciiNull,
        int[]? dimensions,
        ReadOnlyMemory<byte> data,
        FitsScaleMode mode,
        int hduIndex)
    {
        Name = name;
        Format = format;
        _spec = spec;
        _offset = offset;
        _stride = stride;
        _heap = heap;
        _scale = scale;
        _zero = zero;
        _null = nullValue;
        _asciiNull = asciiNull;
        Dimensions = dimensions;
        _data = data;
        _mode = mode;
        _hduIndex = hduIndex;
        Repeat = spec.Variable ? 1 : spec.Repeat;
        IsVariableLength = spec.Variable;
    }

    public string Name { get; }

    public double Scale => _scale;

    public string Format { get; }

    public int Repeat { get; }

    public bool IsVariableLength { get; }

    public int[]? Dimensions { get; }

    public int Count(int row)
    {
        if (!IsVariableLength)
        {
            if (_spec.Code == 'A')
            {
                int piece = _spec.SubstringWidth == 0 ? Math.Max(_spec.Repeat, 1) : _spec.SubstringWidth;
                return Math.Max(1, _spec.Repeat / piece);
            }

            return _spec.Repeat;
        }

        return ReadDescriptor(row).Count;
    }

    public bool IsNull(int row, int element = 0)
    {
        if (_spec.Ascii)
        {
            var field = AsciiField(row);
            return IsAsciiNull(field);
        }

        if (IsVariableLength)
        {
            var descriptor = ReadDescriptor(row);
            if (element < 0 || element >= descriptor.Count)
            {
                throw OutOfRange(element);
            }

            if (_null is null || !IsIntegerCode(_spec.ElementCode))
            {
                return false;
            }

            return ReadHeapInteger(descriptor, element) == _null.Value;
        }

        if (_null is null || !IsIntegerCode(_spec.Code))
        {
            return _spec.Code == 'L' && LogicalByte(row, element) == 0;
        }

        return ReadStoredInteger(row, element) == _null.Value;
    }

    public long ReadInteger(int row, int element = 0)
    {
        if (IsNull(row, element))
        {
            throw new FitsException($"Column '{Name}' row {row} is null.", new FitsLocation(_hduIndex, Column: Name));
        }

        if (_spec.Ascii)
        {
            return long.Parse(AsciiText(AsciiField(row)), CultureInfo.InvariantCulture);
        }

        return IsVariableLength ? ReadHeapInteger(ReadDescriptor(row), element) : ReadStoredInteger(row, element);
    }

    public Int128 ReadExactInteger(int row, int element = 0)
    {
        long stored = ReadInteger(row, element);
        if (_mode == FitsScaleMode.Stored || _scale != 1 || !Pixels.IsExactInteger(_zero))
        {
            if (_mode == FitsScaleMode.Physical && (_scale != 1 || !Pixels.IsExactInteger(_zero)))
            {
                throw new FitsException($"Column '{Name}' is not an integer translation.", new FitsLocation(_hduIndex, Column: Name));
            }

            return stored;
        }

        return stored + Pixels.ToInt128(_zero);
    }

    public double ReadReal(int row, int element = 0)
    {
        if (IsNull(row, element))
        {
            return double.NaN;
        }

        double stored = _spec.Ascii ? ParseAsciiReal(AsciiField(row)) : ReadStoredReal(row, element);
        if (_mode == FitsScaleMode.Stored)
        {
            return stored;
        }

        return _zero + (_scale * stored);
    }

    public bool ReadLogical(int row, int element = 0)
    {
        byte value = LogicalByte(row, element);
        return value switch
        {
            (byte)'T' => true,
            (byte)'F' => false,
            0 => throw new FitsException($"Column '{Name}' row {row} is null.", new FitsLocation(_hduIndex, Column: Name)),
            _ => throw new FitsException($"Column '{Name}' contains a logical value other than T or F.", new FitsLocation(_hduIndex, Column: Name)),
        };
    }

    public string ReadString(int row, int element = 0)
    {
        if (_spec.Ascii || _spec.Code == 'A')
        {
            if (_spec.Ascii || _spec.SubstringWidth == 0)
            {
                var field = _spec.Ascii ? AsciiField(row) : FixedField(row, 0, _spec.Width);
                return TrimString(field);
            }

            return TrimString(FixedField(row, element, _spec.SubstringWidth));
        }

        if (IsVariableLength && _spec.ElementCode == 'A')
        {
            var descriptor = ReadDescriptor(row);
            if (descriptor.Count == 0)
            {
                return "";
            }

            var bytes = HeapSlice(descriptor.Offset, descriptor.Count);
            return TrimString(bytes);
        }

        throw new FitsException($"Column '{Name}' is not a string column.", new FitsLocation(_hduIndex, Column: Name));
    }

    public FitsComplex ReadComplex(int row, int element = 0)
    {
        char code = IsVariableLength ? _spec.ElementCode : _spec.Code;
        int width = code == 'C' ? 8 : 16;
        ReadOnlySpan<byte> bytes = IsVariableLength
            ? HeapSlice(ReadDescriptor(row).Offset + (element * width), width)
            : FixedField(row, element, width);
        double real;
        double imaginary;
        if (code == 'C')
        {
            real = BinaryPrimitives.ReadSingleBigEndian(bytes);
            imaginary = BinaryPrimitives.ReadSingleBigEndian(bytes[4..]);
        }
        else
        {
            real = BinaryPrimitives.ReadDoubleBigEndian(bytes);
            imaginary = BinaryPrimitives.ReadDoubleBigEndian(bytes[8..]);
        }

        if (_mode == FitsScaleMode.Physical)
        {
            real = _zero + (_scale * real);
        }

        return new FitsComplex(real, imaginary);
    }

    private double ReadStoredReal(int row, int element)
    {
        if (IsVariableLength)
        {
            return ReadHeapReal(ReadDescriptor(row), element);
        }

        if (_spec.Code == 'X')
        {
            return ReadBit(FixedField(row, 0, _spec.Width), element);
        }

        return ReadScalar(_spec.Code, FixedField(row, element, ElementWidth(_spec.Code)));
    }

    private long ReadStoredInteger(int row, int element)
    {
        if (_spec.Code == 'X')
        {
            return ReadBit(FixedField(row, 0, _spec.Width), element);
        }

        return ReadScalarInteger(_spec.Code, FixedField(row, element, ElementWidth(_spec.Code)));
    }

    private long ReadHeapInteger(Descriptor descriptor, int element)
    {
        if (_spec.ElementCode == 'X')
        {
            return ReadBit(HeapSlice(descriptor.Offset, (descriptor.Count + 7) / 8), element);
        }

        int width = ElementWidth(_spec.ElementCode);
        return ReadScalarInteger(_spec.ElementCode, HeapSlice(descriptor.Offset + (element * width), width));
    }

    private double ReadHeapReal(Descriptor descriptor, int element)
    {
        if (_spec.ElementCode == 'X')
        {
            return ReadHeapInteger(descriptor, element);
        }

        int width = ElementWidth(_spec.ElementCode);
        return ReadScalar(_spec.ElementCode, HeapSlice(descriptor.Offset + (element * width), width));
    }

    private ReadOnlySpan<byte> FixedField(int row, int element, int width)
    {
        int start = (row * _stride) + _offset + (element * width);
        return _data.Span.Slice(start, width);
    }

    private ReadOnlySpan<byte> AsciiField(int row)
    {
        int start = (row * _stride) + _offset;
        return _data.Span.Slice(start, _spec.Width);
    }

    private ReadOnlySpan<byte> HeapSlice(long offset, int count)
    {
        int start = checked(_heap + (int)offset);
        return _data.Span.Slice(start, count);
    }

    private byte LogicalByte(int row, int element)
    {
        if (_spec.Ascii)
        {
            var field = TrimEnd(AsciiField(row));
            return field.Length == 0 ? (byte)0 : field[0];
        }

        return FixedField(row, element, 1)[0];
    }

    private Descriptor ReadDescriptor(int row)
    {
        var field = FixedField(row, 0, _spec.Code == 'Q' ? 16 : 8);
        if (_spec.Code == 'P')
        {
            return new Descriptor(BinaryPrimitives.ReadInt32BigEndian(field), BinaryPrimitives.ReadInt32BigEndian(field[4..]));
        }

        long count = BinaryPrimitives.ReadInt64BigEndian(field);
        long offset = BinaryPrimitives.ReadInt64BigEndian(field[8..]);
        if (count > int.MaxValue)
        {
            throw new FitsException($"Column '{Name}' has a variable array longer than a single buffer.", new FitsLocation(_hduIndex, Column: Name));
        }

        return new Descriptor((int)count, offset);
    }

    internal byte[] ReadVariableBytes(int row)
    {
        var descriptor = ReadDescriptor(row);
        if (descriptor.Count == 0)
        {
            return [];
        }

        int width = ElementWidth(_spec.ElementCode);
        return HeapSlice(descriptor.Offset, descriptor.Count * width).ToArray();
    }

    internal byte[] ReadHeap(long offset, int count) => HeapSlice(offset, count).ToArray();

    private bool IsAsciiNull(ReadOnlySpan<byte> field)
    {
        if (IsBlank(field))
        {
            return true;
        }

        var text = TrimString(field).Trim();
        if (_asciiNull is not null && text == _asciiNull.Trim())
        {
            return true;
        }

        return _null is long marker
            && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
            && parsed == marker;
    }

    private double ParseAsciiReal(ReadOnlySpan<byte> field)
    {
        var text = TrimString(field).Replace('D', 'E').Replace('d', 'e');
        return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private static string AsciiText(ReadOnlySpan<byte> field) => TrimString(field);

    private static double ReadScalar(char code, ReadOnlySpan<byte> bytes)
    {
        return code switch
        {
            'B' => bytes[0],
            'I' => BinaryPrimitives.ReadInt16BigEndian(bytes),
            'J' => BinaryPrimitives.ReadInt32BigEndian(bytes),
            'K' => BinaryPrimitives.ReadInt64BigEndian(bytes),
            'E' => BinaryPrimitives.ReadSingleBigEndian(bytes),
            'D' => BinaryPrimitives.ReadDoubleBigEndian(bytes),
            'L' => bytes[0] == (byte)'T' ? 1 : 0,
            'X' => bytes[0],
            _ => throw new FitsException($"Column type {code} is not a scalar number.", new FitsLocation()),
        };
    }

    private static long ReadScalarInteger(char code, ReadOnlySpan<byte> bytes)
    {
        return code switch
        {
            'B' => bytes[0],
            'I' => BinaryPrimitives.ReadInt16BigEndian(bytes),
            'J' => BinaryPrimitives.ReadInt32BigEndian(bytes),
            'K' => BinaryPrimitives.ReadInt64BigEndian(bytes),
            'X' => bytes[0] & 1,
            _ => throw new FitsException($"Column type {code} is not an integer.", new FitsLocation()),
        };
    }

    private static int ReadBit(ReadOnlySpan<byte> bytes, int bitIndex)
    {
        int bucket = bitIndex / 8;
        int shift = 7 - (bitIndex % 8);
        return (bytes[bucket] >> shift) & 1;
    }

    private static int ElementWidth(char code) => code switch
    {
        'L' or 'B' or 'A' or 'X' => 1,
        'I' => 2,
        'J' or 'E' => 4,
        'K' or 'D' or 'C' => 8,
        'M' => 16,
        _ => throw new FitsException($"Column type {code} has no fixed width.", new FitsLocation()),
    };

    private static bool IsIntegerCode(char code) => code is 'B' or 'I' or 'J' or 'K' or 'X';

    private static bool IsBlank(ReadOnlySpan<byte> field)
    {
        foreach (byte current in field)
        {
            if (current != (byte)' ')
            {
                return false;
            }
        }

        return true;
    }

    private static string TrimString(ReadOnlySpan<byte> field)
    {
        int end = field.Length;
        while (end > 0 && field[end - 1] is (byte)' ' or 0)
        {
            end--;
        }

        return Encoding.ASCII.GetString(field[..end]);
    }

    private static ReadOnlySpan<byte> TrimEnd(ReadOnlySpan<byte> field)
    {
        int end = field.Length;
        while (end > 0 && field[end - 1] == (byte)' ')
        {
            end--;
        }

        return field[..end];
    }

    private FitsException OutOfRange(int element) =>
        new($"Column '{Name}' has no element {element}.", new FitsLocation(_hduIndex, Column: Name));

    private readonly record struct Descriptor(int Count, long Offset);
}

internal readonly struct ColumnSpec
{
    public char Code { get; init; }

    public char ElementCode { get; init; }

    public int Repeat { get; init; }

    public int Width { get; init; }

    public bool Variable { get; init; }

    public bool Ascii { get; init; }

    public int SubstringWidth { get; init; }
}

internal static class ColumnFormats
{
    public static ColumnSpec Parse(string format, bool ascii)
    {
        var trimmed = format.Trim().ToUpperInvariant();
        if (ascii)
        {
            return ParseAscii(trimmed);
        }

        int index = 0;
        int repeat = ReadRepeat(trimmed, ref index);
        if (index >= trimmed.Length)
        {
            throw new FitsException($"TFORM '{format}' is missing a type code.", new FitsLocation());
        }

        char code = trimmed[index++];
        if (code is 'P' or 'Q')
        {
            char element = index < trimmed.Length && char.IsLetter(trimmed[index]) ? trimmed[index] : 'B';
            if (repeat != 1)
            {
                throw new FitsException($"TFORM '{format}' has more than one variable-length descriptor.", new FitsLocation());
            }

            return new ColumnSpec
            {
                Code = code,
                ElementCode = element,
                Repeat = 1,
                Width = code == 'P' ? 8 : 16,
                Variable = true,
            };
        }

        if (code is not ('L' or 'X' or 'B' or 'I' or 'J' or 'K' or 'A' or 'E' or 'D' or 'C' or 'M'))
        {
            throw new FitsException($"TFORM '{format}' has an unknown type code.", new FitsLocation());
        }

        int substring = 0;
        if (code == 'A' && index < trimmed.Length && char.IsDigit(trimmed[index]))
        {
            substring = ReadRepeat(trimmed, ref index);
            if (substring < 1 || repeat % substring != 0)
            {
                throw new FitsException($"TFORM '{format}' does not divide into equal character fields.", new FitsLocation());
            }
        }

        return new ColumnSpec
        {
            Code = code,
            Repeat = repeat,
            Width = Width(code, repeat),
            SubstringWidth = substring,
        };
    }

    public static int[] ParseDimensions(string text)
    {
        var body = text.Trim();
        if (body.StartsWith('(') && body.EndsWith(')'))
        {
            body = body[1..^1];
        }

        var parts = body.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var axes = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            axes[i] = int.Parse(parts[i], CultureInfo.InvariantCulture);
        }

        return axes;
    }

    private static ColumnSpec ParseAscii(string format)
    {
        char code = format[0];
        if (code is not ('A' or 'I' or 'F' or 'E' or 'D'))
        {
            throw new FitsException($"ASCII TFORM '{format}' is not A, I, F, E, or D.", new FitsLocation());
        }

        int dot = format.IndexOf('.');
        int widthEnd = dot > 0 ? dot : format.Length;
        int width = int.Parse(format[1..widthEnd], CultureInfo.InvariantCulture);
        return new ColumnSpec { Code = code, Repeat = 1, Width = width, Ascii = true };
    }

    private static int ReadRepeat(string format, ref int index)
    {
        int repeat = 0;
        bool any = false;
        while (index < format.Length && char.IsDigit(format[index]))
        {
            any = true;
            repeat = (repeat * 10) + (format[index] - '0');
            index++;
        }

        return any ? repeat : 1;
    }

    private static int Width(char code, int repeat) => code switch
    {
        'L' or 'B' or 'A' => repeat,
        'X' => (repeat + 7) / 8,
        'I' => repeat * 2,
        'J' or 'E' => repeat * 4,
        'K' or 'D' or 'C' => repeat * 8,
        'M' => repeat * 16,
        _ => throw new FitsException($"Column type {code} has no width.", new FitsLocation()),
    };
}
