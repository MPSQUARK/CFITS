using System.Buffers.Binary;
using System.IO.Compression;

namespace CFITS.Compression;

internal static class TableCompression
{
    public static bool IsCompressedTable(FitsHeader header) =>
        header.TryGetCard("ZTABLE", out var table) && table.Kind == FitsValueKind.Logical && table.Logical;

    public static FitsTable Decompress(FitsHeader header, FitsTable compressed, FitsScaleMode mode)
    {
        int rows = checked((int)header.GetInt64("ZNAXIS2"));
        int rowWidth = checked((int)header.GetInt64("ZNAXIS1"));
        int tileLength = checked((int)header.GetInt64("ZTILELEN"));
        if (rows < 0 || rowWidth < 0 || tileLength < 1)
        {
            throw new FitsException("ZTILELEN must contain at least one row.", new FitsLocation(Keyword: "ZTILELEN"));
        }

        int expectedTiles = rows == 0 ? 0 : checked((rows + tileLength - 1) / tileLength);
        if (compressed.RowCount != expectedTiles)
        {
            throw new FitsException($"Compressed table has {compressed.RowCount} tiles; expected {expectedTiles}.", new FitsLocation());
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
            cursor = checked(cursor + specs[field].Width);
        }

        if (rows > 0 && cursor != rowWidth)
        {
            throw new FitsException("The ZFORMn widths do not add up to ZNAXIS1.", new FitsLocation(Keyword: "ZNAXIS1"));
        }

        int bodyLength = checked(rowWidth * rows);
        int declaredHeap = header.TryGetCard("ZPCOUNT", out var heapCard) ? checked((int)heapCard.GetInt64()) : 0;
        if (declaredHeap < 0)
        {
            throw new FitsException("ZPCOUNT must not be negative.", new FitsLocation(Keyword: "ZPCOUNT"));
        }

        var data = new byte[checked(bodyLength + declaredHeap)];
        Span<byte> body = data.AsSpan(0, bodyLength);
        Span<byte> heap = data.AsSpan(bodyLength, declaredHeap);
        int blockSize = BlockSize(header);
        int offset = 0;
        for (int field = 0; field < fields; field++)
        {
            ExpandColumn(compressed.Columns[field], compressed.RowCount, specs[field], algorithms[field], offset, rows, rowWidth, tileLength, blockSize, body, heap);
            offset += specs[field].Width;
        }

        var logical = LogicalHeader(header, fields, names, bodyLength);
        var shape = new HduShape { Bitpix = 8, Axes = [rowWidth, rows], Gcount = 1, Pcount = declaredHeap };
        return FitsTable.Create(logical, data, ascii: false, shape, mode);
    }

    private static void ExpandColumn(FitsColumn column, int tiles, ColumnSpec spec, string algorithm, int offset, int rows, int rowWidth, int tileLength, int blockSize, Span<byte> body, Span<byte> heap)
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
                ExpandVariable(column, spec, algorithm, element, stored, start, tileRows, offset, rowWidth, blockSize, body, heap);
            }
            else
            {
                byte[] plain = Expand(algorithm, stored, tileRows * spec.Width, ElementBytes(element), Numeric(element), blockSize);
                for (int row = 0; row < tileRows; row++)
                {
                    plain.AsSpan(row * spec.Width, spec.Width).CopyTo(body.Slice(((start + row) * rowWidth) + offset, spec.Width));
                }
            }
        }
    }

    private static void ExpandVariable(FitsColumn column, ColumnSpec spec, string algorithm, char element, byte[] stored, int start, int tileRows, int offset, int rowWidth, int blockSize, Span<byte> body, Span<byte> heap)
    {
        byte[] bundle = Gunzip(stored);
        int descriptor = spec.Width;
        int needed = checked((tileRows * descriptor) + (tileRows * descriptor));
        if (bundle.Length < needed)
        {
            throw new FitsException("A compressed variable-length column is missing its descriptors.", new FitsLocation(Column: column.Name));
        }

        int originalBase = checked(tileRows * descriptor);
        for (int row = 0; row < tileRows; row++)
        {
            int compressedAt = row * descriptor;
            int valueWidth = descriptor >> 1;
            long compressedCount = ReadDescriptorValue(bundle.AsSpan(compressedAt, valueWidth), valueWidth);
            long compressedOffset = ReadDescriptorValue(bundle.AsSpan(compressedAt + valueWidth, valueWidth), valueWidth);
            int originalAt = checked(originalBase + (row * descriptor));
            bundle.AsSpan(originalAt, descriptor).CopyTo(body.Slice(((start + row) * rowWidth) + offset, descriptor));
            if (compressedCount < 0 || compressedOffset < 0 || compressedCount > int.MaxValue)
            {
                throw new FitsException("A compressed variable-length descriptor is invalid.", new FitsLocation(Column: column.Name));
            }

            long elements = ReadDescriptorValue(bundle.AsSpan(originalAt, valueWidth), valueWidth);
            long heapOffset = ReadDescriptorValue(bundle.AsSpan(originalAt + valueWidth, valueWidth), valueWidth);
            if (elements < 0 || elements > int.MaxValue || heapOffset < 0)
            {
                throw new FitsException("A compressed variable-length descriptor is invalid.", new FitsLocation(Column: column.Name));
            }

            if (compressedCount == 0)
            {
                if (elements != 0)
                {
                    throw new FitsException("A compressed variable-length descriptor has data without a payload.", new FitsLocation(Column: column.Name));
                }

                continue;
            }

            byte[] payload = column.ReadHeap(compressedOffset, checked((int)compressedCount));
            int width = ElementBytes(element);
            byte[] plain = Expand(algorithm, payload, checked((int)elements * width), width, Numeric(element), blockSize);
            Place(heap, heapOffset, plain);
        }
    }

    private static long ReadDescriptorValue(ReadOnlySpan<byte> bytes, int width) => width switch
    {
        4 => BinaryPrimitives.ReadInt32BigEndian(bytes),
        8 => BinaryPrimitives.ReadInt64BigEndian(bytes),
        _ => throw new FitsException("A compressed variable-length descriptor has an invalid width.", new FitsLocation()),
    };

    private static void Place(Span<byte> heap, long offset, byte[] plain)
    {
        if (offset < 0)
        {
            throw new FitsException("A compressed variable-length descriptor has a negative heap offset.", new FitsLocation());
        }

        long end;
        try
        {
            end = checked(offset + plain.Length);
        }
        catch (OverflowException exception)
        {
            throw new FitsException("A compressed variable-length descriptor has an overflowing heap range.", new FitsLocation(), exception);
        }

        if (offset > int.MaxValue || end > int.MaxValue)
        {
            throw new FitsException("A compressed variable-length descriptor has a heap range outside a single buffer.", new FitsLocation());
        }

        if (end > heap.Length)
        {
            throw new FitsException("A compressed variable-length descriptor exceeds the declared heap.", new FitsLocation());
        }

        plain.CopyTo(heap[(int)offset..]);
    }

    private static byte[] Expand(string algorithm, byte[] compressed, int outputBytes, int elementBytes, bool numeric, int blockSize)
    {
        string name = algorithm.Trim().Replace(' ', '_').ToUpperInvariant();
        if (name is "NOCOMPRESS")
        {
            return RequireLength(compressed, outputBytes);
        }

        if (name is "GZIP_1" or "GZIP_2")
        {
            byte[] plain = GunzipExact(compressed, outputBytes);
            if (name == "GZIP_2" && numeric && elementBytes > 1)
            {
                plain = Unshuffle(plain, elementBytes);
            }

            return RequireLength(plain, outputBytes);
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

    private static byte[] RequireLength(byte[] bytes, int expected)
    {
        if (bytes.Length != expected)
        {
            throw new FitsException($"Decoded table column has {bytes.Length} bytes; expected {expected}.", new FitsLocation());
        }

        return bytes;
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

    private static byte[] GunzipExact(byte[] compressed, int length)
    {
        var output = new byte[length];
        using var input = new MemoryStream(compressed, writable: false);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        int read = 0;
        while (read < output.Length)
        {
            int count = gzip.Read(output.AsSpan(read));
            if (count == 0)
            {
                throw new FitsException($"Decoded table column has {read} bytes; expected {length}.", new FitsLocation());
            }

            read += count;
        }

        if (gzip.ReadByte() >= 0)
        {
            throw new FitsException($"Decoded table column is longer than the expected {length} bytes.", new FitsLocation());
        }

        return output;
    }

    private static byte[] Unshuffle(byte[] shuffled, int width)
    {
        if (width <= 0 || shuffled.Length % width != 0)
        {
            throw new FitsException("A shuffled table column has an invalid length.", new FitsLocation());
        }

        int count = shuffled.Length / width;
        var bytes = new byte[shuffled.Length];
        int sourceOffset = 0;
        for (int item = 0; item < count; item++)
        {
            int destinationOffset = item * width;
            for (int byteIndex = 0; byteIndex < width; byteIndex++)
            {
                bytes[destinationOffset + byteIndex] = shuffled[sourceOffset];
                sourceOffset += count;
            }

            sourceOffset = item + 1;
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
