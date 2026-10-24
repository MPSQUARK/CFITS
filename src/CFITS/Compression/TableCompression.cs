using System.Buffers.Binary;
using System.IO.Compression;

namespace CFITS;

internal static class TableCompression
{
    public static bool IsCompressedTable(FitsHeader header) =>
        header.TryGetCard("ZTABLE", out var table) && table.Kind == FitsValueKind.Logical && table.Logical;

    public static FitsTable Decompress(FitsHeader header, FitsTable compressed, FitsScaleMode mode)
    {
        int rows = checked((int)header.GetInt64("ZNAXIS2"));
        int rowWidth = checked((int)header.GetInt64("ZNAXIS1"));
        int tileLength = checked((int)header.GetInt64("ZTILELEN"));
        if (tileLength < 1)
        {
            throw new FitsException("ZTILELEN must contain at least one row.", new FitsLocation(Keyword: "ZTILELEN"));
        }

        int fields = compressed.Columns.Count;
        var specs = new ColumnSpec[fields];
        var algorithms = new string[fields];
        var names = new string[fields];
        int cursor = 0;
        for (int field = 0; field < fields; field++)
        {
            int number = field + 1;
            specs[field] = ColumnFormats.Parse(header.GetString($"ZFORM{number}"), ascii: false);
            algorithms[field] = header.GetString($"ZCTYP{number}");
            names[field] = header.TryGetCard($"TTYPE{number}", out var name) ? name.GetString() ?? $"Column{number}" : $"Column{number}";
            cursor += specs[field].Width;
        }

        if (rows > 0 && cursor != rowWidth)
        {
            throw new FitsException("The ZFORMn widths do not add up to ZNAXIS1.", new FitsLocation(Keyword: "ZNAXIS1"));
        }

        var body = new byte[rowWidth * rows];
        int declaredHeap = header.TryGetCard("ZPCOUNT", out var heapCard) ? checked((int)heapCard.GetInt64()) : 0;
        var heap = new byte[Math.Max(0, declaredHeap)];
        int blockSize = BlockSize(header);
        int offset = 0;
        for (int field = 0; field < fields; field++)
        {
            ExpandColumn(compressed.Columns[field], compressed.RowCount, specs[field], algorithms[field], offset, rows, rowWidth, tileLength, blockSize, body, ref heap);
            offset += specs[field].Width;
        }

        var logical = LogicalHeader(header, fields, names, rowWidth * rows);
        var data = new byte[body.Length + heap.Length];
        body.CopyTo(data);
        heap.CopyTo(data.AsSpan(body.Length));
        var shape = new HduShape { Bitpix = 8, Axes = [rowWidth, rows], Gcount = 1, Pcount = heap.Length };
        return FitsTable.Create(logical, data, ascii: false, shape, mode);
    }

    private static void ExpandColumn(FitsColumn column, int tiles, ColumnSpec spec, string algorithm, int offset, int rows, int rowWidth, int tileLength, int blockSize, byte[] body, ref byte[] heap)
    {
        char element = spec.Variable ? spec.ElementCode : spec.Code;
        for (int tile = 0; tile < tiles; tile++)
        {
            int start = tile * tileLength;
            if (start >= rows)
            {
                break;
            }

            int tileRows = Math.Min(tileLength, rows - start);
            byte[] stored = column.ReadVariableBytes(tile);
            if (spec.Variable)
            {
                ExpandVariable(column, spec, algorithm, element, stored, start, tileRows, offset, rowWidth, blockSize, body, ref heap);
            }
            else
            {
                byte[] plain = Expand(algorithm, stored, tileRows * spec.Width, ElementBytes(element), Numeric(element), blockSize);
                for (int row = 0; row < tileRows; row++)
                {
                    plain.AsSpan(row * spec.Width, spec.Width).CopyTo(body.AsSpan(((start + row) * rowWidth) + offset, spec.Width));
                }
            }
        }
    }

    private static void ExpandVariable(FitsColumn column, ColumnSpec spec, string algorithm, char element, byte[] stored, int start, int tileRows, int offset, int rowWidth, int blockSize, byte[] body, ref byte[] heap)
    {
        byte[] bundle = Gunzip(stored);
        int descriptor = spec.Width;
        int needed = (tileRows * 16) + (tileRows * descriptor);
        if (bundle.Length < needed)
        {
            throw new FitsException("A compressed variable-length column is missing its descriptors.", new FitsLocation(Column: column.Name));
        }

        for (int row = 0; row < tileRows; row++)
        {
            long compressedCount = BinaryPrimitives.ReadInt64BigEndian(bundle.AsSpan(row * 16, 8));
            long compressedOffset = BinaryPrimitives.ReadInt64BigEndian(bundle.AsSpan((row * 16) + 8, 8));
            int originalAt = (tileRows * 16) + (row * descriptor);
            bundle.AsSpan(originalAt, descriptor).CopyTo(body.AsSpan(((start + row) * rowWidth) + offset, descriptor));
            if (compressedCount == 0)
            {
                continue;
            }

            byte[] payload = column.ReadHeap(compressedOffset, checked((int)compressedCount));
            long elements = descriptor == 16
                ? BinaryPrimitives.ReadInt64BigEndian(bundle.AsSpan(originalAt, 8))
                : BinaryPrimitives.ReadInt32BigEndian(bundle.AsSpan(originalAt, 4));
            long heapOffset = descriptor == 16
                ? BinaryPrimitives.ReadInt64BigEndian(bundle.AsSpan(originalAt + 8, 8))
                : BinaryPrimitives.ReadInt32BigEndian(bundle.AsSpan(originalAt + 4, 4));
            int width = ElementBytes(element);
            byte[] plain = Expand(algorithm, payload, checked((int)elements) * width, width, Numeric(element), blockSize);
            Place(ref heap, heapOffset, plain);
        }
    }

    private static void Place(ref byte[] heap, long offset, byte[] plain)
    {
        long end = offset + plain.Length;
        if (end > heap.Length)
        {
            Array.Resize(ref heap, checked((int)end));
        }

        plain.CopyTo(heap.AsSpan(checked((int)offset)));
    }

    private static byte[] Expand(string algorithm, byte[] compressed, int outputBytes, int elementBytes, bool numeric, int blockSize)
    {
        string name = algorithm.Trim().Replace(' ', '_').ToUpperInvariant();
        if (name is "NOCOMPRESS")
        {
            return compressed;
        }

        if (name is "GZIP_1" or "GZIP_2")
        {
            byte[] plain = Gunzip(compressed);
            if (name == "GZIP_2" && numeric && elementBytes > 1)
            {
                plain = Unshuffle(plain, elementBytes);
            }

            return plain;
        }

        if (name is "RICE_1" or "RICE_ONE")
        {
            if (elementBytes is not (1 or 2 or 4))
            {
                throw new FitsException("Rice compression of a table column supports 1-, 2-, and 4-byte integers.", new FitsLocation());
            }

            int pixels = outputBytes / elementBytes;
            uint[] values = Rice.Decompress(compressed, pixels, elementBytes, blockSize);
            var bytes = new byte[outputBytes];
            for (int index = 0; index < pixels; index++)
            {
                WriteInteger(bytes.AsSpan(index * elementBytes, elementBytes), values[index]);
            }

            return bytes;
        }

        throw new FitsException($"Table compression algorithm '{algorithm}' is not supported.", new FitsLocation());
    }

    private static FitsHeader LogicalHeader(FitsHeader source, int fields, string[] names, int heapStart)
    {
        var builder = FitsHeader.Builder();
        builder.Integer("TFIELDS", fields);
        builder.Integer("THEAP", heapStart);
        for (int field = 0; field < fields; field++)
        {
            int number = field + 1;
            builder.Text($"TFORM{number}", source.GetString($"ZFORM{number}").Trim());
            builder.Text($"TTYPE{number}", names[field]);
            CopyReal(source, builder, $"TSCAL{number}");
            CopyReal(source, builder, $"TZERO{number}");
            if (source.TryGetCard($"TNULL{number}", out var nullCard))
            {
                if (nullCard.Kind == FitsValueKind.Integer)
                {
                    builder.Integer($"TNULL{number}", nullCard.Integer);
                }
                else if (nullCard.GetString() is string text)
                {
                    builder.Text($"TNULL{number}", text);
                }
            }

            if (source.TryGetCard($"TDIM{number}", out var dimensions) && dimensions.GetString() is string shape)
            {
                builder.Text($"TDIM{number}", shape);
            }
        }

        return builder.Build();
    }

    private static void CopyReal(FitsHeader source, FitsHeaderBuilder builder, string keyword)
    {
        if (source.TryGetCard(keyword, out var card) && card.Kind is FitsValueKind.Real or FitsValueKind.Integer)
        {
            builder.Real(keyword, card.GetDouble());
        }
    }

    private static int BlockSize(FitsHeader header)
    {
        for (int index = 1; index < 1000; index++)
        {
            if (!header.TryGetCard($"ZNAME{index}", out var name))
            {
                return 32;
            }

            if (string.Equals(name.GetString(), "BLOCKSIZE", StringComparison.OrdinalIgnoreCase) && header.TryGetCard($"ZVAL{index}", out var value) && value.Kind == FitsValueKind.Integer)
            {
                return checked((int)value.Integer);
            }
        }

        return 32;
    }

    private static byte[] Gunzip(byte[] compressed)
    {
        using var input = new MemoryStream(compressed);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] Unshuffle(byte[] shuffled, int width)
    {
        int count = shuffled.Length / width;
        var bytes = new byte[shuffled.Length];
        for (int item = 0; item < count; item++)
        {
            for (int byteIndex = 0; byteIndex < width; byteIndex++)
            {
                bytes[(item * width) + byteIndex] = shuffled[(byteIndex * count) + item];
            }
        }

        return bytes;
    }

    private static int ElementBytes(char code) => code switch
    {
        'L' or 'B' or 'A' or 'X' => 1,
        'I' => 2,
        'J' or 'E' => 4,
        'K' or 'D' or 'C' => 8,
        'M' => 16,
        _ => throw new FitsException($"Column type '{code}' cannot be decompressed as a byte stream.", new FitsLocation()),
    };

    private static bool Numeric(char code) => code is 'B' or 'I' or 'J' or 'K' or 'E' or 'D' or 'C' or 'M';

    private static void WriteInteger(Span<byte> destination, uint value)
    {
        for (int index = destination.Length - 1; index >= 0; index--)
        {
            destination[index] = (byte)value;
            value >>= 8;
        }
    }
}
