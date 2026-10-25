using System.Buffers.Binary;
using System.Buffers.Text;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace CFITS;

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
    private readonly int _rows;
    private readonly int _hduIndex;
    private int _cachedDescriptorRow = -1;
    private Descriptor _cachedDescriptor;

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
        int rows,
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
        _rows = rows;
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

    public bool CanCopyInteger => IsFixedScalarInteger();

    public bool CanCopyReal => IsFixedScalarReal();

    public int Count(int row)
    {
        EnsureRow(row);
        if (!IsVariableLength)
        {
            if (_spec.Code == 'A')
            {
                return _spec.SubstringWidth == 0 ? 1 : Math.Max(1, _spec.Repeat / _spec.SubstringWidth);
            }

            return _spec.Repeat;
        }

        return CachedDescriptor(row).Count;
    }

    public bool IsNull(int row, int element = 0)
    {
        if (IsVariableLength)
        {
            EnsureRow(row);
            var descriptor = CachedDescriptor(row);
            EnsureDescriptorElement(descriptor, element);
            return IsNull(descriptor, element);
        }

        EnsureElement(row, element);
        if (_spec.Ascii)
        {
            var field = AsciiField(row);
            return IsAsciiNull(field);
        }

        if (_null is null || !IsIntegerCode(_spec.Code))
        {
            return _spec.Code == 'L' && LogicalByte(row, element) == (byte)' ';
        }

        return ReadStoredInteger(row, element) == _null.Value;
    }

    public long ReadInteger(int row, int element = 0)
    {
        if (!IsVariableLength && !_spec.Ascii && _null is null && _spec.Code is 'B' or 'I' or 'J' or 'K')
        {
            EnsureFixed(row, element);
            return ReadStoredInteger(row, element);
        }

        if (IsVariableLength)
        {
            EnsureRow(row);
            var descriptor = CachedDescriptor(row);
            EnsureDescriptorElement(descriptor, element);
            if (IsNull(descriptor, element))
            {
                throw new FitsException($"Column '{Name}' row {row} is null.", new FitsLocation(_hduIndex, Column: Name));
            }

            return ReadHeapInteger(descriptor, element);
        }

        EnsureElement(row, element);
        if (IsNull(row, element))
        {
            throw new FitsException($"Column '{Name}' row {row} is null.", new FitsLocation(_hduIndex, Column: Name));
        }

        if (_spec.Ascii)
        {
            return long.Parse(AsciiText(AsciiField(row)), CultureInfo.InvariantCulture);
        }

        return ReadStoredInteger(row, element);
    }

    public bool TryCopyInteger(Span<long> destination)
    {
        EnsureBulkDestination(destination);
        if (!IsFixedScalarInteger())
        {
            return false;
        }

        for (int row = 0; row < _rows; row++)
        {
            long stored = ReadStoredInteger(row, 0);
            if (_null is long marker && stored == marker)
            {
                throw new FitsException($"Column '{Name}' row {row} is null.", new FitsLocation(_hduIndex, Column: Name));
            }

            destination[row] = stored;
        }

        return true;
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
        if (!IsVariableLength && !_spec.Ascii && _null is null && _spec.Code is 'B' or 'I' or 'J' or 'K' or 'E' or 'D')
        {
            EnsureFixed(row, element);
            double direct = ReadStoredReal(row, element);
            return _mode == FitsScaleMode.Stored ? direct : _zero + (_scale * direct);
        }

        if (_spec.Ascii && element == 0 && _null is null && _asciiNull is null)
        {
            EnsureRow(row);
            var field = AsciiField(row);
            if (IsBlank(field))
            {
                return double.NaN;
            }

            double ascii = ParseAsciiReal(field);
            return _mode == FitsScaleMode.Stored ? ascii : _zero + (_scale * ascii);
        }

        if (IsVariableLength)
        {
            EnsureRow(row);
            var descriptor = CachedDescriptor(row);
            EnsureDescriptorElement(descriptor, element);
            if (IsNull(descriptor, element))
            {
                return double.NaN;
            }

            double variable = ReadHeapReal(descriptor, element);
            return _mode == FitsScaleMode.Stored ? variable : _zero + (_scale * variable);
        }

        EnsureElement(row, element);
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

    public bool TryCopyReal(Span<double> destination)
    {
        EnsureBulkDestination(destination);
        if (!IsFixedScalarReal())
        {
            return false;
        }

        bool physical = _mode == FitsScaleMode.Physical;
        for (int row = 0; row < _rows; row++)
        {
            long storedInteger = 0;
            if (IsIntegerCode(_spec.Code))
            {
                storedInteger = ReadStoredInteger(row, 0);
            }

            double stored = IsIntegerCode(_spec.Code)
                ? storedInteger
                : ReadStoredReal(row, 0);
            destination[row] = physical ? _zero + (_scale * stored) : stored;
            if (_null is long marker && IsIntegerCode(_spec.Code) && storedInteger == marker)
            {
                destination[row] = double.NaN;
            }
        }

        return true;
    }

    public bool ReadLogical(int row, int element = 0)
    {
        EnsureElement(row, element);
        byte value = LogicalByte(row, element);
        return value switch
        {
            (byte)'T' => true,
            (byte)'F' => false,
            (byte)' ' or 0 => throw new FitsException($"Column '{Name}' row {row} is null.", new FitsLocation(_hduIndex, Column: Name)),
            _ => throw new FitsException($"Column '{Name}' contains a logical value other than T or F.", new FitsLocation(_hduIndex, Column: Name)),
        };
    }

    public string ReadString(int row, int element = 0)
    {
        EnsureRow(row);
        if (!IsVariableLength || _spec.ElementCode != 'A')
        {
            EnsureElement(row, element);
        }
        else if (element != 0)
        {
            throw OutOfRange(element);
        }

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
            var descriptor = CachedDescriptor(row);
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
        return (FitsComplex)ReadComplexValue(row, element);
    }

    public Complex ReadComplexValue(int row, int element = 0)
    {
        EnsureElement(row, element);
        char code = IsVariableLength ? _spec.ElementCode : _spec.Code;
        int width = code == 'C' ? 8 : 16;
        ReadOnlySpan<byte> bytes = IsVariableLength
            ? HeapSlice(HeapElementOffset(CachedDescriptor(row), element, width), width)
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
            imaginary = _zero + (_scale * imaginary);
        }

        return new Complex(real, imaginary);
    }

    private double ReadStoredReal(int row, int element)
    {
        if (IsVariableLength)
        {
            return ReadHeapReal(CachedDescriptor(row), element);
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
            int byteCount = (descriptor.Count / 8) + (descriptor.Count % 8 == 0 ? 0 : 1);
            return ReadBit(HeapSlice(descriptor.Offset, byteCount), element);
        }

        int width = ElementWidth(_spec.ElementCode);
        return ReadScalarInteger(_spec.ElementCode, HeapSlice(HeapElementOffset(descriptor, element, width), width));
    }

    private void EnsureDescriptorElement(Descriptor descriptor, int element)
    {
        if ((uint)element >= (uint)descriptor.Count)
        {
            throw OutOfRange(element);
        }
    }

    private double ReadHeapReal(Descriptor descriptor, int element)
    {
        if (_spec.ElementCode == 'X')
        {
            return ReadHeapInteger(descriptor, element);
        }

        int width = ElementWidth(_spec.ElementCode);
        return ReadScalar(_spec.ElementCode, HeapSlice(HeapElementOffset(descriptor, element, width), width));
    }

    private bool IsNull(Descriptor descriptor, int element) =>
        _null is long marker
        && IsIntegerCode(_spec.ElementCode)
        && ReadHeapInteger(descriptor, element) == marker;

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
        if (offset < 0 || count < 0)
        {
            throw new FitsException($"Column '{Name}' has an invalid heap range.", new FitsLocation(_hduIndex, Column: Name));
        }

        long start;
        long end;
        try
        {
            start = checked((long)_heap + offset);
            end = checked(start + count);
        }
        catch (OverflowException exception)
        {
            throw new FitsException($"Column '{Name}' has an overflowing heap range.", new FitsLocation(_hduIndex, Column: Name), exception);
        }

        if (start > _data.Length || end > _data.Length)
        {
            throw new FitsException($"Column '{Name}' references data outside the heap.", new FitsLocation(_hduIndex, Column: Name));
        }

        return _data.Span.Slice(checked((int)start), count);
    }

    private byte LogicalByte(int row, int element)
    {
        if (_spec.Ascii)
        {
            var field = TrimEnd(AsciiField(row));
            return field.Length == 0 ? (byte)0 : field[0];
        }

        if (IsVariableLength)
        {
            var descriptor = CachedDescriptor(row);
            return HeapSlice(HeapElementOffset(descriptor, element, 1), 1)[0];
        }

        return FixedField(row, element, 1)[0];
    }

    private Descriptor CachedDescriptor(int row)
    {
        if (_cachedDescriptorRow == row)
        {
            return _cachedDescriptor;
        }

        _cachedDescriptor = ReadDescriptor(row);
        _cachedDescriptorRow = row;
        return _cachedDescriptor;
    }

    private void EnsureFixed(int row, int element)
    {
        if ((uint)row >= (uint)_rows)
        {
            throw new FitsException($"Column '{Name}' has no row {row}.", new FitsLocation(_hduIndex, Column: Name));
        }

        if ((uint)element >= (uint)_spec.Repeat)
        {
            throw OutOfRange(element);
        }
    }

    private Descriptor ReadDescriptor(int row)
    {
        var field = FixedField(row, 0, _spec.Code == 'Q' ? 16 : 8);
        if (_spec.Code == 'P')
        {
            int pCount = BinaryPrimitives.ReadInt32BigEndian(field);
            int pOffset = BinaryPrimitives.ReadInt32BigEndian(field[4..]);
            if (pCount < 0 || pOffset < 0)
            {
                throw new FitsException($"Column '{Name}' has an invalid variable-length descriptor.", new FitsLocation(_hduIndex, Column: Name));
            }

            EnsureMaximumLength(pCount);
            return new Descriptor(pCount, pOffset);
        }

        long count = BinaryPrimitives.ReadInt64BigEndian(field);
        long offset = BinaryPrimitives.ReadInt64BigEndian(field[8..]);
        if (count < 0 || count > int.MaxValue || offset < 0)
        {
            throw new FitsException($"Column '{Name}' has a variable array longer than a single buffer.", new FitsLocation(_hduIndex, Column: Name));
        }

        EnsureMaximumLength((int)count);
        return new Descriptor((int)count, offset);
    }

    private void EnsureMaximumLength(int count)
    {
        if (_spec.MaximumElements is int maximum && count > maximum)
        {
            throw new FitsException($"Column '{Name}' exceeds its declared variable-array limit of {maximum} elements.", new FitsLocation(_hduIndex, Column: Name));
        }
    }

    private long HeapElementOffset(Descriptor descriptor, int element, int width)
    {
        try
        {
            return checked(descriptor.Offset + ((long)element * width));
        }
        catch (OverflowException exception)
        {
            throw new FitsException($"Column '{Name}' has an overflowing heap element offset.", new FitsLocation(_hduIndex, Column: Name), exception);
        }
    }

    internal byte[] ReadVariableBytes(int row)
    {
        var descriptor = CachedDescriptor(row);
        if (descriptor.Count == 0)
        {
            return [];
        }

        int byteCount;
        try
        {
            byteCount = _spec.ElementCode == 'X'
                ? (descriptor.Count / 8) + (descriptor.Count % 8 == 0 ? 0 : 1)
                : checked(descriptor.Count * ElementWidth(_spec.ElementCode));
        }
        catch (OverflowException exception)
        {
            throw new FitsException($"Column '{Name}' has an overflowing variable-array size.", new FitsLocation(_hduIndex, Column: Name), exception);
        }

        return HeapSlice(descriptor.Offset, byteCount).ToArray();
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

    private void EnsureBulkDestination<T>(Span<T> destination)
    {
        if (destination.Length < _rows)
        {
            throw new FitsException("The destination is shorter than the column.", new FitsLocation(_hduIndex, Column: Name));
        }
    }

    private bool IsFixedScalarInteger() =>
        !_spec.Ascii && !IsVariableLength && _spec.Repeat == 1 && _spec.Code is 'B' or 'I' or 'J' or 'K';

    private bool IsFixedScalarReal() =>
        !_spec.Ascii && !IsVariableLength && _spec.Repeat == 1 && _spec.Code is 'B' or 'I' or 'J' or 'K' or 'E' or 'D';

    private static double ParseAsciiReal(ReadOnlySpan<byte> field)
    {
        int end = field.Length;
        while (end > 0 && field[end - 1] is (byte)' ' or 0)
        {
            end--;
        }

        int start = 0;
        while (start < end && field[start] == (byte)' ')
        {
            start++;
        }

        ReadOnlySpan<byte> token = field.Slice(start, end - start);
        Span<byte> scratch = stackalloc byte[128];
        if (token.Length <= scratch.Length)
        {
            token.CopyTo(scratch);
            Span<byte> normalized = scratch[..token.Length];
            for (int index = 0; index < normalized.Length; index++)
            {
                if (normalized[index] is (byte)'D' or (byte)'d')
                {
                    normalized[index] = (byte)'E';
                }
            }

            if (Utf8Parser.TryParse(normalized, out double parsed, out int consumed) && consumed == normalized.Length)
            {
                return parsed;
            }
        }

        string text = Encoding.ASCII.GetString(token).Replace('D', 'E').Replace('d', 'e');
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

    private void EnsureRow(int row)
    {
        if ((uint)row >= (uint)_rows)
        {
            throw new FitsException($"Column '{Name}' has no row {row}.", new FitsLocation(_hduIndex, Column: Name));
        }
    }

    private void EnsureElement(int row, int element)
    {
        EnsureRow(row);
        int count = Count(row);
        if ((uint)element >= (uint)count)
        {
            throw OutOfRange(element);
        }
    }

    private readonly record struct Descriptor(int Count, long Offset);
}
