using System.Buffers.Binary;
using System.Numerics;

namespace CFITS;

public sealed class BinaryTableBuilder
{
    private readonly List<BinaryColumn> _columns = [];

    public BinaryTableBuilder Add(string name, ReadOnlySpan<bool> values)
    {
        var copy = values.ToArray();
        var cells = new byte[copy.Length];
        for (int index = 0; index < copy.Length; index++)
        {
            cells[index] = copy[index] ? (byte)'T' : (byte)'F';
        }

        _columns.Add(new BinaryColumn(name, "L", 1, copy.Length, cells));
        return this;
    }

    public BinaryTableBuilder Add(string name, ReadOnlySpan<bool?> values)
    {
        var copy = values.ToArray();
        var cells = new byte[copy.Length];
        for (int index = 0; index < copy.Length; index++)
        {
            cells[index] = copy[index] switch
            {
                true => (byte)'T',
                false => (byte)'F',
                _ => (byte)' ',
            };
        }

        _columns.Add(new BinaryColumn(name, "L", 1, copy.Length, cells));
        return this;
    }

    public BinaryTableBuilder Add(string name, ReadOnlySpan<byte> values)
    {
        var copy = values.ToArray();
        _columns.Add(new BinaryColumn(name, "B", 1, copy.Length, copy));
        return this;
    }

    public BinaryTableBuilder Add(string name, ReadOnlySpan<short> values)
    {
        var copy = values.ToArray();
        var cells = new byte[checked(copy.Length * 2)];
        for (int row = 0; row < copy.Length; row++)
        {
            BinaryPrimitives.WriteInt16BigEndian(cells.AsSpan(row * 2, 2), copy[row]);
        }

        _columns.Add(new BinaryColumn(name, "I", 2, copy.Length, cells));
        return this;
    }

    public BinaryTableBuilder Add(string name, ReadOnlySpan<int> values)
    {
        var copy = values.ToArray();
        var cells = new byte[checked(copy.Length * 4)];
        for (int row = 0; row < copy.Length; row++)
        {
            BinaryPrimitives.WriteInt32BigEndian(cells.AsSpan(row * 4, 4), copy[row]);
        }

        _columns.Add(new BinaryColumn(name, "J", 4, copy.Length, cells));
        return this;
    }

    public BinaryTableBuilder Add(string name, ReadOnlySpan<long> values)
    {
        var copy = values.ToArray();
        var cells = new byte[checked(copy.Length * 8)];
        for (int row = 0; row < copy.Length; row++)
        {
            BinaryPrimitives.WriteInt64BigEndian(cells.AsSpan(row * 8, 8), copy[row]);
        }

        _columns.Add(new BinaryColumn(name, "K", 8, copy.Length, cells));
        return this;
    }

    public BinaryTableBuilder Add(string name, ReadOnlySpan<float> values)
    {
        var copy = values.ToArray();
        var cells = new byte[checked(copy.Length * 4)];
        for (int row = 0; row < copy.Length; row++)
        {
            BinaryPrimitives.WriteSingleBigEndian(cells.AsSpan(row * 4, 4), copy[row]);
        }

        _columns.Add(new BinaryColumn(name, "E", 4, copy.Length, cells));
        return this;
    }

    public BinaryTableBuilder Add(string name, ReadOnlySpan<double> values)
    {
        var copy = values.ToArray();
        var cells = new byte[checked(copy.Length * 8)];
        for (int row = 0; row < copy.Length; row++)
        {
            BinaryPrimitives.WriteDoubleBigEndian(cells.AsSpan(row * 8, 8), copy[row]);
        }

        _columns.Add(new BinaryColumn(name, "D", 8, copy.Length, cells));
        return this;
    }

    public BinaryTableBuilder AddText(string name, int width, IReadOnlyList<string[]> rows)
    {
        if (width <= 0 || rows.Count == 0 || rows[0].Length == 0)
        {
            throw new FitsException("A character array column needs a positive width and at least one field.", new FitsLocation(Column: name));
        }

        int count = rows[0].Length;
        int repeat;
        int cellCount;
        try
        {
            repeat = checked(count * width);
            cellCount = checked(rows.Count * repeat);
        }
        catch (OverflowException exception)
        {
            throw new FitsException("The character array column is too large.", new FitsLocation(Column: name), exception);
        }

        var cells = new byte[cellCount];
        for (int row = 0; row < rows.Count; row++)
        {
            if (rows[row].Length != count)
            {
                throw new FitsException("Character array rows must have the same number of fields.", new FitsLocation(Column: name));
            }

            for (int field = 0; field < count; field++)
            {
                var text = rows[row][field] ?? "";
                if (text.Length > width)
                {
                    throw new FitsException("A character field is wider than its declared width.", new FitsLocation(Column: name));
                }

                int start = (row * repeat) + (field * width);
                for (int i = 0; i < width; i++)
                {
                    cells[start + i] = i < text.Length ? (byte)text[i] : (byte)' ';
                }
            }
        }

        _columns.Add(new BinaryColumn(name, $"{repeat}A{width}", repeat, rows.Count, cells));
        return this;
    }

    public BinaryTableBuilder Add(string name, ReadOnlySpan<string> values, int width)
    {
        if (width <= 0)
        {
            throw new FitsException("A character column needs a positive width.", new FitsLocation(Column: name));
        }

        int rows = values.Length;
        int cellCount;
        try
        {
            cellCount = checked(rows * width);
        }
        catch (OverflowException exception)
        {
            throw new FitsException("The character column is too large.", new FitsLocation(Column: name), exception);
        }

        var cells = new byte[cellCount];
        for (int row = 0; row < rows; row++)
        {
            var text = values[row] ?? "";
            if (text.Length > width)
            {
                throw new FitsException("A character field is wider than its declared width.", new FitsLocation(Column: name));
            }

            for (int i = 0; i < width && i < text.Length; i++)
            {
                cells[(row * width) + i] = (byte)text[i];
            }

            for (int i = text.Length; i < width; i++)
            {
                cells[(row * width) + i] = (byte)' ';
            }
        }

        _columns.Add(new BinaryColumn(name, $"{width}A", width, rows, cells));
        return this;
    }

    public BinaryTableBuilder AddVector(string name, int repeat, ReadOnlySpan<int> values, int[] dimensions)
    {
        ValidateVector(name, repeat, values.Length, dimensions);

        int rows = values.Length / repeat;
        var copy = values.ToArray();
        int width = checked(repeat * 4);
        var cells = new byte[checked(rows * width)];
        for (int row = 0; row < rows; row++)
        {
            for (int index = 0; index < repeat; index++)
            {
                BinaryPrimitives.WriteInt32BigEndian(
                    cells.AsSpan((row * width) + (index * 4), 4),
                    copy[(row * repeat) + index]);
            }
        }

        _columns.Add(new BinaryColumn(name, $"{repeat}J", width, rows, cells)
        {
            Dimensions = "(" + string.Join(',', dimensions) + ")",
        });
        return this;
    }

    public BinaryTableBuilder AddVector(string name, int repeat, ReadOnlySpan<float> values, int[] dimensions)
    {
        ValidateVector(name, repeat, values.Length, dimensions);

        int rows = values.Length / repeat;
        var copy = values.ToArray();
        int width = checked(repeat * 4);
        var cells = new byte[checked(rows * width)];
        for (int row = 0; row < rows; row++)
        {
            for (int index = 0; index < repeat; index++)
            {
                BinaryPrimitives.WriteSingleBigEndian(
                    cells.AsSpan((row * width) + (index * 4), 4),
                    copy[(row * repeat) + index]);
            }
        }

        _columns.Add(new BinaryColumn(name, $"{repeat}E", width, rows, cells)
        {
            Dimensions = "(" + string.Join(',', dimensions) + ")",
        });
        return this;
    }

    public BinaryTableBuilder AddVector(string name, int repeat, ReadOnlySpan<double> values, int[] dimensions)
    {
        ValidateVector(name, repeat, values.Length, dimensions);

        int rows = values.Length / repeat;
        var copy = values.ToArray();
        int width = checked(repeat * 8);
        var cells = new byte[checked(rows * width)];
        for (int row = 0; row < rows; row++)
        {
            for (int index = 0; index < repeat; index++)
            {
                BinaryPrimitives.WriteDoubleBigEndian(
                    cells.AsSpan((row * width) + (index * 8), 8),
                    copy[(row * repeat) + index]);
            }
        }

        _columns.Add(new BinaryColumn(name, $"{repeat}D", width, rows, cells)
        {
            Dimensions = "(" + string.Join(',', dimensions) + ")",
        });
        return this;
    }

    public BinaryTableBuilder AddComplex(string name, ReadOnlySpan<FitsComplex> values)
    {
        var copy = values.ToArray();
        var cells = new byte[checked(copy.Length * 16)];
        for (int row = 0; row < copy.Length; row++)
        {
            int offset = row * 16;
            BinaryPrimitives.WriteDoubleBigEndian(cells.AsSpan(offset, 8), copy[row].Real);
            BinaryPrimitives.WriteDoubleBigEndian(cells.AsSpan(offset + 8, 8), copy[row].Imaginary);
        }

        _columns.Add(new BinaryColumn(name, "M", 16, copy.Length, cells));
        return this;
    }

    public BinaryTableBuilder AddComplexValues(string name, ReadOnlySpan<Complex> values)
    {
        var copy = values.ToArray();
        var cells = new byte[checked(copy.Length * 16)];
        for (int row = 0; row < copy.Length; row++)
        {
            int offset = row * 16;
            BinaryPrimitives.WriteDoubleBigEndian(cells.AsSpan(offset, 8), copy[row].Real);
            BinaryPrimitives.WriteDoubleBigEndian(cells.AsSpan(offset + 8, 8), copy[row].Imaginary);
        }

        _columns.Add(new BinaryColumn(name, "M", 16, copy.Length, cells));
        return this;
    }

    public BinaryTableBuilder AddSingleComplex(string name, ReadOnlySpan<FitsComplex> values)
    {
        var copy = values.ToArray();
        var cells = new byte[checked(copy.Length * 8)];
        for (int row = 0; row < copy.Length; row++)
        {
            int offset = row * 8;
            BinaryPrimitives.WriteSingleBigEndian(cells.AsSpan(offset, 4), (float)copy[row].Real);
            BinaryPrimitives.WriteSingleBigEndian(cells.AsSpan(offset + 4, 4), (float)copy[row].Imaginary);
        }

        _columns.Add(new BinaryColumn(name, "C", 8, copy.Length, cells));
        return this;
    }

    public BinaryTableBuilder AddBits(string name, int bitCount, IReadOnlyList<bool[]> rows)
    {
        if (bitCount <= 0)
        {
            throw new FitsException("A bit column needs a positive bit count.", new FitsLocation(Column: name));
        }

        int width;
        int cellCount;
        try
        {
            width = checked((int)((bitCount + 7L) / 8L));
            cellCount = checked(rows.Count * width);
        }
        catch (OverflowException exception)
        {
            throw new FitsException("The bit column is too large.", new FitsLocation(Column: name), exception);
        }

        var cells = new byte[cellCount];
        for (int row = 0; row < rows.Count; row++)
        {
            if (rows[row].Length != bitCount)
            {
                throw new FitsException("Bit-column rows must match the declared bit count.", new FitsLocation(Column: name));
            }

            int destination = row * width;
            int bit = 0;
            for (; bit + 8 <= bitCount; bit += 8)
            {
                byte packed = 0;
                bool[] source = rows[row];
                for (int offset = 0; offset < 8; offset++)
                {
                    if (source[bit + offset])
                    {
                        packed |= (byte)(0x80 >> offset);
                    }
                }

                cells[destination++] = packed;
            }

            if (bit < bitCount)
            {
                byte packed = 0;
                bool[] source = rows[row];
                for (int offset = 0; bit + offset < bitCount; offset++)
                {
                    if (source[bit + offset])
                    {
                        packed |= (byte)(0x80 >> offset);
                    }
                }

                cells[destination] = packed;
            }
        }

        _columns.Add(new BinaryColumn(name, $"{bitCount}X", width, rows.Count, cells));
        return this;
    }

    private static void ValidateVector(string name, int repeat, int valueCount, int[] dimensions)
    {
        if (repeat < 1 || valueCount % repeat != 0 || dimensions.Length == 0)
        {
            throw new FitsException("A fixed-length vector has invalid dimensions.", new FitsLocation(Column: name));
        }

        long product = 1;
        for (int index = 0; index < dimensions.Length; index++)
        {
            if (dimensions[index] <= 0)
            {
                throw new FitsException("Vector dimensions must be positive.", new FitsLocation(Column: name));
            }

            product = checked(product * dimensions[index]);
        }

        if (product != repeat)
        {
            throw new FitsException("Vector dimensions must multiply to the repeat count.", new FitsLocation(Column: name));
        }
    }

    public BinaryTableBuilder AddVariable(string name, IReadOnlyList<int[]> rows)
    {
        var counts = rows.Select(static row => row.Length).ToArray();
        var lengths = GetVariableLengths(counts, 4);
        var heap = new byte[GetHeapSize(lengths)];
        int cursor = 0;
        for (int row = 0; row < rows.Count; row++)
        {
            int[] source = rows[row];
            for (int index = 0; index < source.Length; index++)
            {
                BinaryPrimitives.WriteInt32BigEndian(heap.AsSpan(cursor + (index * 4), 4), source[index]);
            }

            cursor += lengths[row];
        }

        return AddVariableColumn(name, "PJ", false, rows.Count, heap, counts, lengths);
    }

    public BinaryTableBuilder AddVariable(string name, IReadOnlyList<double[]> rows)
    {
        var counts = rows.Select(static row => row.Length).ToArray();
        var lengths = GetVariableLengths(counts, 8);
        var heap = new byte[GetHeapSize(lengths)];
        int cursor = 0;
        for (int row = 0; row < rows.Count; row++)
        {
            double[] source = rows[row];
            for (int index = 0; index < source.Length; index++)
            {
                BinaryPrimitives.WriteDoubleBigEndian(heap.AsSpan(cursor + (index * 8), 8), source[index]);
            }

            cursor += lengths[row];
        }

        return AddVariableColumn(name, "QD", true, rows.Count, heap, counts, lengths);
    }

    public BinaryTableBuilder AddVariable(string name, IReadOnlyList<float[]> rows)
    {
        var counts = rows.Select(static row => row.Length).ToArray();
        var lengths = GetVariableLengths(counts, 4);
        var heap = new byte[GetHeapSize(lengths)];
        int cursor = 0;
        for (int row = 0; row < rows.Count; row++)
        {
            float[] source = rows[row];
            for (int index = 0; index < source.Length; index++)
            {
                BinaryPrimitives.WriteSingleBigEndian(heap.AsSpan(cursor + (index * 4), 4), source[index]);
            }

            cursor += lengths[row];
        }

        return AddVariableColumn(name, "PE", false, rows.Count, heap, counts, lengths);
    }

    public BinaryTableBuilder AddVariable(string name, IReadOnlyList<string> rows)
    {
        var counts = rows.Select(static row => row.Length).ToArray();
        return AddVariable(name, "PA", 'P', 1, rows.Count, counts, (row, index, destination) => destination[0] = (byte)rows[row][index]);
    }

    public BinaryTableBuilder AddVariableBits(string name, IReadOnlyList<bool[]> rows)
    {
        var counts = rows.Select(static row => row.Length).ToArray();
        return AddVariable(name, "PX", 'P', 0, rows.Count, counts, static (row, index, destination) => { }, packBits: rows);
    }

    internal TablePayload Encode()
    {
        if (_columns.Count == 0)
        {
            throw new FitsException("A binary table needs at least one column.", new FitsLocation());
        }

        int rows = _columns[0].Rows;
        if (_columns.Any(column => column.Rows != rows))
        {
            throw new FitsException("Binary-table columns must all have the same number of rows.", new FitsLocation());
        }
        int stride = 0;
        int heapSize = 0;
        foreach (var column in _columns)
        {
            stride += column.Width;
            heapSize += column.Heap.Length;
        }

        var data = new byte[(stride * rows) + heapSize];
        var header = FitsHeader.Builder()
            .Text("XTENSION", "BINTABLE")
            .Integer("BITPIX", 8)
            .Integer("NAXIS", 2)
            .Integer("NAXIS1", stride)
            .Integer("NAXIS2", rows)
            .Integer("PCOUNT", heapSize)
            .Integer("GCOUNT", 1)
            .Integer("TFIELDS", _columns.Count);
        var offsets = new int[_columns.Count];
        int offset = 0;
        for (int field = 0; field < _columns.Count; field++)
        {
            var column = _columns[field];
            offsets[field] = offset;
            header.Text($"TTYPE{field + 1}", column.Name);
            header.Text($"TFORM{field + 1}", column.Format);
            if (column.Dimensions is not null)
            {
                header.Text($"TDIM{field + 1}", column.Dimensions);
            }
            offset += column.Width;
        }

        // Write fixed cells row-major so each row is touched once and wide tables
        // do not repeatedly jump by the complete row stride.
        for (int row = 0; row < rows; row++)
        {
            int rowOffset = row * stride;
            for (int field = 0; field < _columns.Count; field++)
            {
                BinaryColumn column = _columns[field];
                if (!column.Variable)
                {
                    column.Cells.AsSpan(row * column.Width, column.Width)
                        .CopyTo(data.AsSpan(rowOffset + offsets[field], column.Width));
                }
            }
        }

        int heapCursor = 0;
        for (int field = 0; field < _columns.Count; field++)
        {
            var column = _columns[field];
            if (column.Variable)
            {
                int local = 0;
                for (int row = 0; row < rows; row++)
                {
                    int descriptor = (row * stride) + offsets[field];
                    int byteOffset = heapCursor + local;
                    if (column.WideDescriptor)
                    {
                        BinaryPrimitives.WriteInt64BigEndian(data.AsSpan(descriptor, 8), column.Counts[row]);
                        BinaryPrimitives.WriteInt64BigEndian(data.AsSpan(descriptor + 8, 8), byteOffset);
                    }
                    else
                    {
                        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(descriptor, 4), column.Counts[row]);
                        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(descriptor + 4, 4), byteOffset);
                    }

                    local += column.HeapRowLengths[row];
                }

                column.Heap.CopyTo(data.AsSpan((stride * rows) + heapCursor));
                heapCursor += column.Heap.Length;
            }
        }

        return new TablePayload(header.Build(), data);
    }

    private BinaryTableBuilder AddVariable(string name, string format, char descriptor, int elementWidth, int rows, int[] counts, Action<int, int, Span<byte>> writeElement, IReadOnlyList<bool[]>? packBits = null)
    {
        var lengths = GetVariableLengths(counts, elementWidth);
        int heapBytes = GetHeapSize(lengths);
        var heap = new byte[heapBytes];
        int cursor = 0;
        for (int row = 0; row < rows; row++)
        {
            if (packBits is not null)
            {
                int destination = cursor;
                bool[] source = packBits[row];
                int bit = 0;
                for (; bit + 8 <= counts[row]; bit += 8)
                {
                    byte packed = 0;
                    for (int offset = 0; offset < 8; offset++)
                    {
                        if (source[bit + offset])
                        {
                            packed |= (byte)(0x80 >> offset);
                        }
                    }

                    heap[destination++] = packed;
                }

                if (bit < counts[row])
                {
                    byte packed = 0;
                    for (int offset = 0; bit + offset < counts[row]; offset++)
                    {
                        if (source[bit + offset])
                        {
                            packed |= (byte)(0x80 >> offset);
                        }
                    }

                    heap[destination] = packed;
                }
            }
            else
            {
                for (int index = 0; index < counts[row]; index++)
                {
                    writeElement(row, index, heap.AsSpan(cursor + (index * elementWidth), elementWidth));
                }
            }

            cursor += lengths[row];
        }

        return AddVariableColumn(name, format, descriptor == 'Q', rows, heap, counts, lengths);
    }

    private BinaryTableBuilder AddVariableColumn(
        string name,
        string format,
        bool wideDescriptor,
        int rows,
        byte[] heap,
        int[] counts,
        int[] lengths)
    {
        _columns.Add(new BinaryColumn(name, format, wideDescriptor ? 16 : 8, rows, [])
        {
            Variable = true,
            WideDescriptor = wideDescriptor,
            Heap = heap,
            Counts = counts,
            HeapRowLengths = lengths,
        });
        return this;
    }

    private static int[] GetVariableLengths(int[] counts, int elementWidth)
    {
        var lengths = new int[counts.Length];
        for (int row = 0; row < counts.Length; row++)
        {
            lengths[row] = elementWidth == 0
                ? checked((counts[row] + 7) / 8)
                : checked(counts[row] * elementWidth);
        }

        return lengths;
    }

    private static int GetHeapSize(int[] lengths)
    {
        int total = 0;
        for (int row = 0; row < lengths.Length; row++)
        {
            total = checked(total + lengths[row]);
        }

        return total;
    }

    private BinaryTableBuilder AddRaw(string name, string format, int width, int rows, Action<int, Span<byte>> write)
    {
        var cells = new byte[rows * width];
        for (int row = 0; row < rows; row++)
        {
            write(row, cells.AsSpan(row * width, width));
        }

        _columns.Add(new BinaryColumn(name, format, width, rows, cells));
        return this;
    }

}
