using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace CFITS;

public sealed class FitsDocumentBuilder
{
    private readonly List<Action<Stream>> _writers = [];
    private bool _hasPrimary;
    private bool _checksum;

    public static FitsDocumentBuilder Create() => new();

    public FitsDocumentBuilder Checksum()
    {
        _checksum = true;
        return this;
    }

    public FitsDocumentBuilder Image(ReadOnlySpan<int> axes, ReadOnlySpan<byte> values, Action<FitsHeaderBuilder>? header = null) =>
        AddImage(8, axes, values, static (destination, value) => destination[0] = value, header);

    public FitsDocumentBuilder Image(ReadOnlySpan<int> axes, ReadOnlySpan<short> values, Action<FitsHeaderBuilder>? header = null) =>
        AddImage(16, axes, values, static (destination, value) => BinaryPrimitives.WriteInt16BigEndian(destination, value), header);

    public FitsDocumentBuilder Image(ReadOnlySpan<int> axes, ReadOnlySpan<int> values, Action<FitsHeaderBuilder>? header = null) =>
        AddImage(32, axes, values, static (destination, value) => BinaryPrimitives.WriteInt32BigEndian(destination, value), header);

    public FitsDocumentBuilder Image(ReadOnlySpan<int> axes, ReadOnlySpan<long> values, Action<FitsHeaderBuilder>? header = null) =>
        AddImage(64, axes, values, static (destination, value) => BinaryPrimitives.WriteInt64BigEndian(destination, value), header);

    public FitsDocumentBuilder Image(ReadOnlySpan<int> axes, ReadOnlySpan<float> values, Action<FitsHeaderBuilder>? header = null) =>
        AddImage(-32, axes, values, static (destination, value) => BinaryPrimitives.WriteSingleBigEndian(destination, value), header);

    public FitsDocumentBuilder Image(ReadOnlySpan<int> axes, ReadOnlySpan<double> values, Action<FitsHeaderBuilder>? header = null) =>
        AddImage(-64, axes, values, static (destination, value) => BinaryPrimitives.WriteDoubleBigEndian(destination, value), header);

    public FitsDocumentBuilder GzipImage(ReadOnlySpan<int> axes, ReadOnlySpan<short> values) =>
        AddGzipImage(16, axes, values, static (destination, value) => BinaryPrimitives.WriteInt16BigEndian(destination, value), shuffle: false);

    public FitsDocumentBuilder GzipImage(ReadOnlySpan<int> axes, ReadOnlySpan<float> values, bool shuffle = false) =>
        AddGzipImage(-32, axes, values, static (destination, value) => BinaryPrimitives.WriteSingleBigEndian(destination, value), shuffle);

    public FitsDocumentBuilder AsciiTable(AsciiTableBuilder table, Action<FitsHeaderBuilder>? header = null)
    {
        EnsurePrimary();
        var payload = table.Encode();
        _writers.Add(stream => WriteHdu(stream, payload.Header, payload.Data, header, extension: true));
        return this;
    }

    public FitsDocumentBuilder BinaryTable(BinaryTableBuilder table, Action<FitsHeaderBuilder>? header = null)
    {
        EnsurePrimary();
        var payload = table.Encode();
        _writers.Add(stream => WriteHdu(stream, payload.Header, payload.Data, header, extension: true));
        return this;
    }

    public FitsDocumentBuilder RandomGroups(int bitpix, ReadOnlySpan<int> dataAxes, int parameterCount, ReadOnlySpan<double> parameters, ReadOnlySpan<double> data, IReadOnlyList<string>? parameterNames = null, Action<FitsHeaderBuilder>? header = null)
    {
        if (_hasPrimary)
        {
            throw new FitsException("Random groups occupy the primary HDU.", new FitsLocation());
        }

        int dataPerGroup = Product(dataAxes);
        if (parameterCount < 0 || (parameterCount > 0 && parameters.Length % parameterCount != 0))
        {
            throw new FitsException("Group parameters do not fill a whole number of groups.", new FitsLocation());
        }

        int groups = parameterCount == 0 ? 0 : parameters.Length / parameterCount;
        if (parameterCount == 0 && dataPerGroup > 0)
        {
            groups = data.Length / dataPerGroup;
        }
        if (data.Length != groups * dataPerGroup)
        {
            throw new FitsException("Group data does not match the axes and group count.", new FitsLocation());
        }

        var axes = dataAxes.ToArray();
        var parameterCopy = parameters.ToArray();
        var dataCopy = data.ToArray();
        var names = parameterNames?.ToArray();
        _writers.Add(stream => WriteGroups(stream, bitpix, axes, parameterCount, groups, parameterCopy, dataCopy, names, header));
        _hasPrimary = true;
        return this;
    }

    public FitsDocument Build()
    {
        using var stream = new MemoryStream();
        foreach (var write in _writers)
        {
            if (!_checksum)
            {
                write(stream);
                continue;
            }

            using var hdu = new MemoryStream();
            write(hdu);
            byte[] stamped = FitsChecksum.Stamp(hdu.ToArray());
            stream.Write(stamped);
        }

        return FitsDocument.Parse(stream.ToArray());
    }

    private void EnsurePrimary()
    {
        if (_hasPrimary)
        {
            return;
        }

        Image(ReadOnlySpan<int>.Empty, ReadOnlySpan<byte>.Empty, static header => header.Logical("EXTEND", true));
    }

    private FitsDocumentBuilder AddImage<T>(int bitpix, ReadOnlySpan<int> axes, ReadOnlySpan<T> values, Encode<T> encode, Action<FitsHeaderBuilder>? header)
    {
        int count = Product(axes);
        if (values.Length != count)
        {
            throw new FitsException("The image value count does not match the axes.", new FitsLocation());
        }

        int width = Math.Abs(bitpix) / 8;
        var data = new byte[count * width];
        for (int index = 0; index < count; index++)
        {
            encode(data.AsSpan(index * width, width), values[index]);
        }

        bool extension = _hasPrimary;
        var capturedAxes = axes.ToArray();
        var capturedHeader = header;
        _writers.Add(stream =>
        {
            var cards = FitsHeader.Builder();
            if (extension)
            {
            cards.Text("XTENSION", "IMAGE   ");
            }
            else
            {
                cards.Logical("SIMPLE", true);
            }

            cards.Integer("BITPIX", bitpix);
            cards.Integer("NAXIS", capturedAxes.Length);
            for (int axis = 0; axis < capturedAxes.Length; axis++)
            {
                cards.Integer($"NAXIS{axis + 1}", capturedAxes[axis]);
            }

            if (extension)
            {
                cards.Integer("PCOUNT", 0);
                cards.Integer("GCOUNT", 1);
            }

            capturedHeader?.Invoke(cards);
            WriteHdu(stream, cards.Build(), data, extra: null, extension);
        });
        _hasPrimary = true;
        return this;
    }

    private FitsDocumentBuilder AddGzipImage<T>(int bitpix, ReadOnlySpan<int> axes, ReadOnlySpan<T> values, Encode<T> encode, bool shuffle)
    {
        EnsurePrimary();
        int count = Product(axes);
        if (values.Length != count)
        {
            throw new FitsException("The image value count does not match the axes.", new FitsLocation());
        }

        int width = Math.Abs(bitpix) / 8;
        var raw = new byte[count * width];
        for (int index = 0; index < count; index++)
        {
            encode(raw.AsSpan(index * width, width), values[index]);
        }

        if (shuffle)
        {
            raw = Shuffle(raw, width);
        }

        byte[] compressed = Gzip(raw);
        var capturedAxes = axes.ToArray();
        _writers.Add(stream =>
        {
            var cards = FitsHeader.Builder();
            cards.Text("XTENSION", "BINTABLE");
            cards.Integer("BITPIX", 8);
            cards.Integer("NAXIS", 2);
            cards.Integer("NAXIS1", 16);
            cards.Integer("NAXIS2", 1);
            cards.Integer("PCOUNT", compressed.Length);
            cards.Integer("GCOUNT", 1);
            cards.Integer("TFIELDS", 1);
            cards.Text("TTYPE1", "COMPRESSED_DATA");
            cards.Text("TFORM1", "1QB");
            cards.Integer("THEAP", 16);
            cards.Logical("ZIMAGE", true);
            cards.Text("ZCMPTYPE", shuffle ? "GZIP_2" : "GZIP_1");
            cards.Integer("ZBITPIX", bitpix);
            cards.Integer("ZNAXIS", capturedAxes.Length);
            cards.Text("ZNAME1", "BYTEPIX");
            cards.Integer("ZVAL1", width);
            for (int axis = 0; axis < capturedAxes.Length; axis++)
            {
                cards.Integer($"ZNAXIS{axis + 1}", capturedAxes[axis]);
                cards.Integer($"ZTILE{axis + 1}", capturedAxes[axis]);
            }

            var data = new byte[16 + compressed.Length];
            BinaryPrimitives.WriteInt64BigEndian(data, compressed.Length);
            BinaryPrimitives.WriteInt64BigEndian(data.AsSpan(8), 0);
            compressed.CopyTo(data.AsSpan(16));
            WriteHdu(stream, cards.Build(), data, extra: null, extension: true);
        });
        return this;
    }

    private static byte[] Gzip(byte[] raw)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(raw);
        }

        return output.ToArray();
    }

    private static byte[] Shuffle(byte[] source, int width)
    {
        if (width <= 1)
        {
            return source;
        }

        int count = source.Length / width;
        var bytes = new byte[source.Length];
        for (int item = 0; item < count; item++)
        {
            for (int byteIndex = 0; byteIndex < width; byteIndex++)
            {
                bytes[(byteIndex * count) + item] = source[(item * width) + byteIndex];
            }
        }

        return bytes;
    }

    private static void WriteHdu(Stream stream, FitsHeader header, byte[] data, Action<FitsHeaderBuilder>? extra, bool extension)
    {
        if (extra is not null)
        {
            var editor = FitsHeader.Builder();
            Copy(header, editor);
            extra(editor);
            header = editor.Build();
        }

        header.Write(stream);
        stream.Write(data);
        int pad = (int)(FitsBlock.Pad(data.Length) - data.Length);
        if (pad > 0)
        {
            stream.Write(new byte[pad]);
        }
    }

    private static void Copy(FitsHeader source, FitsHeaderBuilder destination)
    {
        foreach (var card in source.Cards)
        {
            switch (card.Kind)
            {
                case FitsValueKind.Logical:
                    destination.Logical(card.Keyword, card.Logical, card.Comment);
                    break;
                case FitsValueKind.Integer:
                    destination.Integer(card.Keyword, card.Integer, card.Comment);
                    break;
                case FitsValueKind.Real:
                    destination.Real(card.Keyword, card.Real, card.Comment);
                    break;
                case FitsValueKind.String:
                    destination.Text(card.Keyword, card.Text ?? "", card.Comment);
                    break;
                case FitsValueKind.Commentary:
                    if (card.Keyword == "COMMENT")
                    {
                        destination.Comment(card.Text ?? "");
                    }
                    else if (card.Keyword == "HISTORY")
                    {
                        destination.History(card.Text ?? "");
                    }
                    else
                    {
                        destination.Blank(card.Text ?? "");
                    }

                    break;
            }
        }
    }

    private static void WriteGroups(Stream stream, int bitpix, int[] dataAxes, int parameterCount, int groups, double[] parameters, double[] data, string[]? names, Action<FitsHeaderBuilder>? extra)
    {
        int dataPerGroup = 1;
        foreach (int axis in dataAxes)
        {
            dataPerGroup *= axis;
        }

        if (dataAxes.Length == 0)
        {
            dataPerGroup = 0;
        }

        int valuesPerGroup = parameterCount + dataPerGroup;
        int width = Math.Abs(bitpix) / 8;
        var payload = new byte[groups * valuesPerGroup * width];
        for (int group = 0; group < groups; group++)
        {
            for (int parameter = 0; parameter < parameterCount; parameter++)
            {
                Pixels.Write(payload, bitpix, (group * valuesPerGroup) + parameter, parameters[(group * parameterCount) + parameter]);
            }

            for (int element = 0; element < dataPerGroup; element++)
            {
                Pixels.Write(payload, bitpix, (group * valuesPerGroup) + parameterCount + element, data[(group * dataPerGroup) + element]);
            }
        }

        var header = FitsHeader.Builder()
            .Logical("SIMPLE", true)
            .Integer("BITPIX", bitpix)
            .Integer("NAXIS", dataAxes.Length + 1)
            .Integer("NAXIS1", 0);
        for (int axis = 0; axis < dataAxes.Length; axis++)
        {
            header.Integer($"NAXIS{axis + 2}", dataAxes[axis]);
        }

        header.Logical("GROUPS", true).Integer("PCOUNT", parameterCount).Integer("GCOUNT", groups);
        for (int parameter = 0; parameter < parameterCount; parameter++)
        {
            header.Text($"PTYPE{parameter + 1}", names is not null && parameter < names.Length ? names[parameter] : $"P{parameter + 1}");
        }

        extra?.Invoke(header);
        header.Build().Write(stream);
        stream.Write(payload);
        int pad = (int)(FitsBlock.Pad(payload.Length) - payload.Length);
        if (pad > 0)
        {
            stream.Write(new byte[pad]);
        }
    }

    private static int Product(ReadOnlySpan<int> axes)
    {
        if (axes.Length == 0)
        {
            return 0;
        }

        int product = 1;
        foreach (int axis in axes)
        {
            product = checked(product * axis);
        }

        return product;
    }

    private delegate void Encode<T>(Span<byte> destination, T value);
}

public sealed class BinaryTableBuilder
{
    private readonly List<BinaryColumn> _columns = [];

    public BinaryTableBuilder Add(string name, ReadOnlySpan<bool> values)
    {
        var copy = values.ToArray();
        return AddRaw(name, "L", 1, copy.Length, (row, destination) => destination[0] = copy[row] ? (byte)'T' : (byte)'F');
    }

    public BinaryTableBuilder Add(string name, ReadOnlySpan<bool?> values)
    {
        var copy = values.ToArray();
        return AddRaw(name, "L", 1, copy.Length, (row, destination) => destination[0] = copy[row] switch
        {
            true => (byte)'T',
            false => (byte)'F',
            _ => 0,
        });
    }

    public BinaryTableBuilder Add(string name, ReadOnlySpan<byte> values)
    {
        var copy = values.ToArray();
        return AddRaw(name, "B", 1, copy.Length, (row, destination) => destination[0] = copy[row]);
    }

    public BinaryTableBuilder Add(string name, ReadOnlySpan<short> values)
    {
        var copy = values.ToArray();
        return AddRaw(name, "I", 2, copy.Length, (row, destination) => BinaryPrimitives.WriteInt16BigEndian(destination, copy[row]));
    }

    public BinaryTableBuilder Add(string name, ReadOnlySpan<int> values)
    {
        var copy = values.ToArray();
        return AddRaw(name, "J", 4, copy.Length, (row, destination) => BinaryPrimitives.WriteInt32BigEndian(destination, copy[row]));
    }

    public BinaryTableBuilder Add(string name, ReadOnlySpan<long> values)
    {
        var copy = values.ToArray();
        return AddRaw(name, "K", 8, copy.Length, (row, destination) => BinaryPrimitives.WriteInt64BigEndian(destination, copy[row]));
    }

    public BinaryTableBuilder Add(string name, ReadOnlySpan<float> values)
    {
        var copy = values.ToArray();
        return AddRaw(name, "E", 4, copy.Length, (row, destination) => BinaryPrimitives.WriteSingleBigEndian(destination, copy[row]));
    }

    public BinaryTableBuilder Add(string name, ReadOnlySpan<double> values)
    {
        var copy = values.ToArray();
        return AddRaw(name, "D", 8, copy.Length, (row, destination) => BinaryPrimitives.WriteDoubleBigEndian(destination, copy[row]));
    }

    public BinaryTableBuilder AddText(string name, int width, IReadOnlyList<string[]> rows)
    {
        if (rows.Count == 0 || rows[0].Length == 0)
        {
            throw new FitsException("A character array column needs at least one field.", new FitsLocation(Column: name));
        }

        int count = rows[0].Length;
        int repeat = count * width;
        var cells = new byte[rows.Count * repeat];
        for (int row = 0; row < rows.Count; row++)
        {
            for (int field = 0; field < count; field++)
            {
                var text = rows[row][field] ?? "";
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
        int rows = values.Length;
        var cells = new byte[rows * width];
        for (int row = 0; row < rows; row++)
        {
            var text = values[row] ?? "";
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
        if (repeat < 1 || values.Length % repeat != 0)
        {
            throw new FitsException("A fixed-length column does not divide into whole rows.", new FitsLocation(Column: name));
        }

        int rows = values.Length / repeat;
        var copy = values.ToArray();
        var added = AddRaw(name, $"{repeat}J", repeat * 4, rows, (row, destination) =>
        {
            for (int index = 0; index < repeat; index++)
            {
                BinaryPrimitives.WriteInt32BigEndian(destination[(index * 4)..], copy[(row * repeat) + index]);
            }
        });
        _columns[^1] = new BinaryColumn(name, $"{repeat}J", repeat * 4, rows, _columns[^1].Cells)
        {
            Dimensions = "(" + string.Join(',', dimensions) + ")",
        };
        return added;
    }

    public BinaryTableBuilder AddVector(string name, int repeat, ReadOnlySpan<float> values, int[] dimensions)
    {
        if (repeat < 1 || values.Length % repeat != 0)
        {
            throw new FitsException("A fixed-length column does not divide into whole rows.", new FitsLocation(Column: name));
        }

        int rows = values.Length / repeat;
        var copy = values.ToArray();
        var added = AddRaw(name, $"{repeat}E", repeat * 4, rows, (row, destination) =>
        {
            for (int index = 0; index < repeat; index++)
            {
                BinaryPrimitives.WriteSingleBigEndian(destination[(index * 4)..], copy[(row * repeat) + index]);
            }
        });
        _columns[^1] = new BinaryColumn(name, $"{repeat}E", repeat * 4, rows, _columns[^1].Cells)
        {
            Dimensions = "(" + string.Join(',', dimensions) + ")",
        };
        return added;
    }

    public BinaryTableBuilder AddVector(string name, int repeat, ReadOnlySpan<double> values, int[] dimensions)
    {
        if (repeat < 1 || values.Length % repeat != 0)
        {
            throw new FitsException("A fixed-length column does not divide into whole rows.", new FitsLocation(Column: name));
        }

        int rows = values.Length / repeat;
        var copy = values.ToArray();
        var added = AddRaw(name, $"{repeat}D", repeat * 8, rows, (row, destination) =>
        {
            for (int index = 0; index < repeat; index++)
            {
                BinaryPrimitives.WriteDoubleBigEndian(destination[(index * 8)..], copy[(row * repeat) + index]);
            }
        });
        _columns[^1] = new BinaryColumn(name, $"{repeat}D", repeat * 8, rows, _columns[^1].Cells)
        {
            Dimensions = "(" + string.Join(',', dimensions) + ")",
        };
        return added;
    }

    public BinaryTableBuilder AddComplex(string name, ReadOnlySpan<FitsComplex> values)
    {
        var copy = values.ToArray();
        return AddRaw(name, "M", 16, copy.Length, (row, destination) =>
        {
            BinaryPrimitives.WriteDoubleBigEndian(destination, copy[row].Real);
            BinaryPrimitives.WriteDoubleBigEndian(destination[8..], copy[row].Imaginary);
        });
    }

    public BinaryTableBuilder AddSingleComplex(string name, ReadOnlySpan<FitsComplex> values)
    {
        var copy = values.ToArray();
        return AddRaw(name, "C", 8, copy.Length, (row, destination) =>
        {
            BinaryPrimitives.WriteSingleBigEndian(destination, (float)copy[row].Real);
            BinaryPrimitives.WriteSingleBigEndian(destination[4..], (float)copy[row].Imaginary);
        });
    }

    public BinaryTableBuilder AddBits(string name, int bitCount, IReadOnlyList<bool[]> rows)
    {
        int width = (bitCount + 7) / 8;
        var cells = new byte[rows.Count * width];
        for (int row = 0; row < rows.Count; row++)
        {
            for (int bit = 0; bit < bitCount; bit++)
            {
                if (rows[row][bit])
                {
                    cells[(row * width) + (bit / 8)] |= (byte)(1 << (7 - (bit % 8)));
                }
            }
        }

        _columns.Add(new BinaryColumn(name, $"{bitCount}X", width, rows.Count, cells));
        return this;
    }

    public BinaryTableBuilder AddVariable(string name, IReadOnlyList<int[]> rows) =>
        AddVariable(name, "PJ", 'P', 4, rows.Count, rows.Select(static row => row.Length).ToArray(), (row, index, destination) => BinaryPrimitives.WriteInt32BigEndian(destination, rows[row][index]));

    public BinaryTableBuilder AddVariable(string name, IReadOnlyList<double[]> rows) =>
        AddVariable(name, "QD", 'Q', 8, rows.Count, rows.Select(static row => row.Length).ToArray(), (row, index, destination) => BinaryPrimitives.WriteDoubleBigEndian(destination, rows[row][index]));

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
        int offset = 0;
        int heapCursor = 0;
        for (int field = 0; field < _columns.Count; field++)
        {
            var column = _columns[field];
            header.Text($"TTYPE{field + 1}", column.Name);
            header.Text($"TFORM{field + 1}", column.Format);
            if (column.Dimensions is not null)
            {
                header.Text($"TDIM{field + 1}", column.Dimensions);
            }
            if (!column.Variable)
            {
                for (int row = 0; row < rows; row++)
                {
                    column.Cells.AsSpan(row * column.Width, column.Width).CopyTo(data.AsSpan((row * stride) + offset, column.Width));
                }
            }
            else
            {
                int local = 0;
                for (int row = 0; row < rows; row++)
                {
                    int descriptor = (row * stride) + offset;
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

            offset += column.Width;
        }

        return new TablePayload(header.Build(), data);
    }

    private BinaryTableBuilder AddVariable(string name, string format, char descriptor, int elementWidth, int rows, int[] counts, Action<int, int, Span<byte>> writeElement, IReadOnlyList<bool[]>? packBits = null)
    {
        var lengths = new int[rows];
        int heapBytes = 0;
        for (int row = 0; row < rows; row++)
        {
            lengths[row] = elementWidth == 0 ? (counts[row] + 7) / 8 : counts[row] * elementWidth;
            heapBytes += lengths[row];
        }

        var heap = new byte[heapBytes];
        int cursor = 0;
        for (int row = 0; row < rows; row++)
        {
            if (packBits is not null)
            {
                for (int bit = 0; bit < counts[row]; bit++)
                {
                    if (packBits[row][bit])
                    {
                        heap[cursor + (bit / 8)] |= (byte)(1 << (7 - (bit % 8)));
                    }
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

        int width = descriptor == 'Q' ? 16 : 8;
        _columns.Add(new BinaryColumn(name, format, width, rows, [])
        {
            Variable = true,
            WideDescriptor = descriptor == 'Q',
            Heap = heap,
            Counts = counts,
            HeapRowLengths = lengths,
        });
        return this;
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

    private sealed class BinaryColumn
    {
        public BinaryColumn(string name, string format, int width, int rows, byte[] cells)
        {
            Name = name;
            Format = format;
            Width = width;
            Rows = rows;
            Cells = cells;
        }

        public string Name { get; }

        public string Format { get; }

        public int Width { get; }

        public int Rows { get; }

        public byte[] Cells { get; }

        public bool Variable { get; init; }

        public bool WideDescriptor { get; init; }

        public byte[] Heap { get; init; } = [];

        public int[] Counts { get; init; } = [];

        public int[] HeapRowLengths { get; init; } = [];

        public string? Dimensions { get; init; }
    }
}

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

    private sealed class AsciiColumn
    {
        public AsciiColumn(string name, string format, int width, int rows, string[] cells)
        {
            Name = name;
            Format = format;
            Width = width;
            Rows = rows;
            Cells = cells;
        }

        public string Name { get; }

        public string Format { get; }

        public int Width { get; }

        public int Rows { get; }

        public string[] Cells { get; }
    }
}

internal readonly record struct TablePayload(FitsHeader Header, byte[] Data);
