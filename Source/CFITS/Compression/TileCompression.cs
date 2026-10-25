using System.IO.Compression;

namespace CFITS.Compression;

internal static class TileCompression
{
    private static readonly float[] Dither = BuildDither();

    public static bool IsCompressedImage(FitsHeader header) =>
        header.TryGetCard("ZIMAGE", out var image) && image.Kind == FitsValueKind.Logical && image.Logical;

    public static FitsImage Decompress(FitsHeader header, FitsTable table, FitsScaleMode mode)
    {
        int bitpix = checked((int)header.GetInt64("ZBITPIX"));
        int axesCount = checked((int)header.GetInt64("ZNAXIS"));
        if (axesCount < 0 || axesCount > 999)
        {
            throw new FitsException("ZNAXIS must be from 0 through 999.", new FitsLocation(Keyword: "ZNAXIS"));
        }

        var axes = new int[axesCount];
        var tile = new int[axesCount];
        for (int axis = 0; axis < axesCount; axis++)
        {
            axes[axis] = checked((int)header.GetInt64($"ZNAXIS{axis + 1}"));
            if (axes[axis] <= 0)
            {
                throw new FitsException($"ZNAXIS{axis + 1} must be positive for a compressed image.", new FitsLocation(Keyword: $"ZNAXIS{axis + 1}"));
            }

            tile[axis] = axis == 0 ? axes[0] : 1;
            if (header.TryGetCard($"ZTILE{axis + 1}", out var tileCard))
            {
                tile[axis] = checked((int)tileCard.GetInt64());
            }

            if (tile[axis] <= 0)
            {
                throw new FitsException($"ZTILE{axis + 1} must be positive.", new FitsLocation(Keyword: $"ZTILE{axis + 1}"));
            }
        }

        string algorithm = header.GetString("ZCMPTYPE").Trim().Replace(' ', '_').ToUpperInvariant();
        int pixels = axes.Length == 0 ? 0 : 1;
        foreach (int axis in axes)
        {
            pixels = checked(pixels * axis);
        }

        int width = Pixels.Width(bitpix);
        var raw = new byte[checked(pixels * width)];
        var counts = new int[axesCount];
        for (int axis = 0; axis < axesCount; axis++)
        {
            counts[axis] = checked((axes[axis] + tile[axis] - 1) / tile[axis]);
        }
        int expectedTiles = 1;
        foreach (int count in counts)
        {
            expectedTiles = checked(expectedTiles * count);
        }

        var data = table.Column("COMPRESSED_DATA");
        FitsColumn? gzip = table.Columns.FirstOrDefault(column => column.Name.Equals("GZIP_COMPRESSED_DATA", StringComparison.OrdinalIgnoreCase));
        FitsColumn? scale = table.Columns.FirstOrDefault(column => column.Name.Equals("ZSCALE", StringComparison.OrdinalIgnoreCase));
        FitsColumn? zero = table.Columns.FirstOrDefault(column => column.Name.Equals("ZZERO", StringComparison.OrdinalIgnoreCase));
        int blockSize = Setting(header, "BLOCKSIZE", 32);
        int bytePix = Setting(header, "BYTEPIX", 4);
        int smooth = Setting(header, "SMOOTH", 0);
        string quantize = header.TryGetCard("ZQUANTIZ", out var quantizeCard) ? quantizeCard.GetString() ?? "NO_DITHER" : "NO_DITHER";
        int ditherSeed = header.TryGetCard("ZDITHER0", out var seedCard) ? checked((int)seedCard.GetInt64()) : 0;
        long? blank = header.TryGetCard("ZBLANK", out var blankCard) && blankCard.Kind == FitsValueKind.Integer ? blankCard.Integer : null;

        int rows = table.RowCount;
        if (rows != expectedTiles)
        {
            throw new FitsException($"Compressed image has {rows} tiles; expected {expectedTiles}.", new FitsLocation());
        }
        var origin = new int[axesCount];
        var span = new int[axesCount];
        for (int row = 0; row < rows; row++)
        {
            TileOrigin(row, counts, tile, origin);
            TileSpan(origin, tile, axes, span);
            int tilePixels = axesCount == 0 ? 0 : 1;
            foreach (int length in span)
            {
                tilePixels = checked(tilePixels * length);
            }

            if (tilePixels == 0)
            {
                continue;
            }

            byte[] compressed = data.ReadVariableBytes(row);
            bool losslessGzip = compressed.Length == 0 && gzip is not null;
            double rowScale = losslessGzip ? 1 : scale?.ReadReal(row) ?? 1;
            double rowZero = losslessGzip ? 0 : zero?.ReadReal(row) ?? 0;
            if (!losslessGzip
                && algorithm is "GZIP_1" or "GZIP_2"
                && rowScale == 1
                && rowZero == 0
                && bytePix == width
                && !quantize.Contains("SUBTRACTIVE", StringComparison.OrdinalIgnoreCase)
                && TryContiguous(axes, origin, span, out int start, out int count)
                && count == tilePixels)
            {
                var target = raw.AsSpan(checked(start * width), checked(tilePixels * width));
                if (algorithm == "GZIP_1")
                {
                    GunzipInto(compressed, target);
                }
                else
                {
                    Unshuffle(GunzipExact(compressed, target.Length), bytePix, target);
                }

                continue;
            }

            var decoded = losslessGzip
                ? new TileBytes(RequireLength(Gunzip(gzip!.ReadVariableBytes(row)), checked(tilePixels * width)), width)
                : Decode(algorithm, compressed, tilePixels, bytePix, blockSize, width, smooth);
            WriteTile(raw, axes, origin, span, bitpix, decoded.Bytes, decoded.Width, quantize, ditherSeed, row + 1, rowScale, rowZero, blank);
        }

        var shape = new HduShape { Bitpix = bitpix, Axes = axes, Gcount = 1 };
        return FitsImage.Create(header, raw, shape, mode);
    }

    private readonly record struct TileBytes(byte[] Bytes, int Width);

    private static TileBytes Decode(string algorithm, byte[] compressed, int pixels, int bytePix, int blockSize, int width, int smooth)
    {
        if (algorithm is "GZIP_1" or "GZIP_2")
        {
            byte[] decoded = Gunzip(compressed);
            byte[] plain = algorithm == "GZIP_2" ? Unshuffle(decoded, bytePix) : decoded;
            return new TileBytes(RequireLength(plain, checked(pixels * bytePix)), bytePix);
        }

        if (algorithm is "RICE_1" or "RICE_ONE")
        {
            uint[] values = Rice.Decompress(compressed, pixels, bytePix, blockSize);
            return new TileBytes(Pack(values, pixels, bytePix), bytePix);
        }

        if (algorithm is "PLIO_1")
        {
            int[] values = Plio.Decompress(compressed, pixels);
            var bytes = new byte[pixels * bytePix];
            for (int index = 0; index < pixels; index++)
            {
                WriteInteger(bytes.AsSpan(index * bytePix, bytePix), values[index]);
            }

            return new TileBytes(bytes, bytePix);
        }

        if (algorithm is "HCOMPRESS_1")
        {
            int[] values = HCompress.Decompress(compressed, smooth);
            if (values.Length != pixels)
            {
                throw new FitsException("An H-compress tile does not match the image tile.", new FitsLocation());
            }

            var bytes = new byte[pixels * 4];
            for (int index = 0; index < pixels; index++)
            {
                WriteInteger(bytes.AsSpan(index * 4, 4), values[index]);
            }

            return new TileBytes(bytes, 4);
        }

        if (algorithm is "NOCOMPRESS")
        {
            return new TileBytes(RequireLength(compressed, checked(pixels * bytePix)), bytePix);
        }

        throw new FitsException($"Compression algorithm '{algorithm}' is not supported yet.", new FitsLocation());
    }

    private static byte[] Pack(uint[] values, int pixels, int bytePix)
    {
        var bytes = new byte[pixels * bytePix];
        for (int index = 0; index < pixels; index++)
        {
            WriteInteger(bytes.AsSpan(index * bytePix, bytePix), values[index]);
        }

        return bytes;
    }

    private static void WriteTile(byte[] destination, int[] axes, int[] origin, int[] span, int bitpix, byte[] decoded, int bytePix, string quantize, int seed, int tileNumber, double scale, double zero, long? blank)
    {
        bool floating = bitpix < 0;
        bool dither = quantize.Contains("SUBTRACTIVE", StringComparison.OrdinalIgnoreCase);
        bool restoreZero = quantize.Contains('2');
        int ditherIndex = 0;
        int sequence = 0;
        if (dither)
        {
            sequence = Math.Abs((tileNumber - 1 + seed) % 10000);
            ditherIndex = (int)(Dither[sequence] * 500);
        }

        int width = Math.Abs(bitpix) / 8;
        int tilePixels = 1;
        foreach (int length in span)
        {
            tilePixels = checked(tilePixels * length);
        }

        bool directBytes = bytePix == width && (bitpix > 0 || (scale == 1 && zero == 0 && !dither));
        if (directBytes && TryContiguous(axes, origin, span, out int start, out int count) && count == tilePixels)
        {
            decoded.AsSpan(0, checked(count * width)).CopyTo(destination.AsSpan(checked(start * width), checked(count * width)));
            return;
        }

        int cursor = 0;

        for (int tileIndex = 0; tileIndex < tilePixels; tileIndex++)
        {
            int address = 0;
            int stride = 1;
            int remaining = tileIndex;
            for (int axis = 0; axis < axes.Length; axis++)
            {
                int coordinate = remaining % span[axis];
                remaining /= span[axis];
                address += (origin[axis] + coordinate) * stride;
                stride *= axes[axis];
            }

            double value;
            if (!floating)
            {
                value = ReadInteger(decoded, cursor, bytePix);
                cursor += bytePix;
            }
            else if (scale == 1 && zero == 0 && !dither)
            {
                value = width == 4
                    ? System.Buffers.Binary.BinaryPrimitives.ReadSingleBigEndian(decoded.AsSpan(cursor, 4))
                    : System.Buffers.Binary.BinaryPrimitives.ReadDoubleBigEndian(decoded.AsSpan(cursor, 8));
                cursor += width;
            }
            else
            {
                long quantized = ReadInteger(decoded, cursor, bytePix);
                cursor += bytePix;
                if (blank is long marker && quantized == marker)
                {
                    value = double.NaN;
                }
                else if (restoreZero && quantized == -2147483647)
                {
                    value = 0;
                }
                else if (dither)
                {
                    float random = Dither[ditherIndex];
                    value = ((quantized - random + 0.5) * scale) + zero;
                    ditherIndex++;
                    if (ditherIndex == 500)
                    {
                        sequence = (sequence + 1) % 10000;
                        ditherIndex = (int)(Dither[sequence] * 500);
                    }
                }
                else
                {
                    value = (quantized * scale) + zero;
                }
            }

            int offset = address * width;
            if (bitpix > 0)
            {
                WriteInteger(destination.AsSpan(offset, width), (long)value);
            }
            else if (bitpix == -32)
            {
                System.Buffers.Binary.BinaryPrimitives.WriteSingleBigEndian(destination.AsSpan(offset, 4), (float)value);
            }
            else
            {
                System.Buffers.Binary.BinaryPrimitives.WriteDoubleBigEndian(destination.AsSpan(offset, 8), value);
            }
        }
    }

    private static byte[] Gunzip(byte[] compressed)
    {
        using var output = new MemoryStream();
        using (var input = new MemoryStream(compressed, writable: false))
        using (var gzip = new GZipStream(input, CompressionMode.Decompress))
        {
            gzip.CopyTo(output);
        }

        return output.ToArray();
    }

    private static byte[] GunzipExact(byte[] compressed, int length)
    {
        var output = new byte[length];
        GunzipInto(compressed, output);
        return output;
    }

    private static void GunzipInto(byte[] compressed, Span<byte> destination)
    {
        using var input = new MemoryStream(compressed, writable: false);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        int read = 0;
        while (read < destination.Length)
        {
            int count = gzip.Read(destination[read..]);
            if (count == 0)
            {
                throw new FitsException($"Decoded tile has {read} bytes; expected {destination.Length}.", new FitsLocation());
            }

            read += count;
        }

        if (gzip.ReadByte() >= 0)
        {
            throw new FitsException($"Decoded tile is longer than the expected {destination.Length} bytes.", new FitsLocation());
        }
    }

    private static bool TryContiguous(int[] axes, int[] origin, int[] span, out int start, out int count)
    {
        start = 0;
        count = 1;
        int stride = 1;
        for (int axis = 0; axis < axes.Length; axis++)
        {
            bool last = axis == axes.Length - 1;
            if (!last && (origin[axis] != 0 || span[axis] != axes[axis]))
            {
                return false;
            }

            start = checked(start + (origin[axis] * stride));
            count = checked(count * span[axis]);
            stride = checked(stride * axes[axis]);
        }

        return true;
    }

    private static void Unshuffle(ReadOnlySpan<byte> shuffled, int width, Span<byte> destination)
    {
        if (width <= 1)
        {
            shuffled.CopyTo(destination);
            return;
        }

        if (shuffled.Length != destination.Length || shuffled.Length % width != 0)
        {
            throw new FitsException("A shuffled tile length is not divisible by its element width.", new FitsLocation());
        }

        int count = shuffled.Length / width;
        int sourceOffset = 0;
        for (int item = 0; item < count; item++)
        {
            int destinationOffset = item * width;
            for (int byteIndex = 0; byteIndex < width; byteIndex++)
            {
                destination[destinationOffset + byteIndex] = shuffled[sourceOffset];
                sourceOffset += count;
            }

            sourceOffset = item + 1;
        }
    }

    private static byte[] Unshuffle(byte[] shuffled, int width)
    {
        if (width <= 1)
        {
            return shuffled;
        }

        if (shuffled.Length % width != 0)
        {
            throw new FitsException("A shuffled tile length is not divisible by its element width.", new FitsLocation());
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

    private static byte[] RequireLength(byte[] bytes, int expected)
    {
        if (bytes.Length != expected)
        {
            throw new FitsException($"Decoded tile has {bytes.Length} bytes; expected {expected}.", new FitsLocation());
        }

        return bytes;
    }

    private static int Setting(FitsHeader header, string name, int fallback)
    {
        for (int index = 1; index < 1000; index++)
        {
            if (!header.TryGetCard($"ZNAME{index}", out var setting))
            {
                return fallback;
            }

            if (string.Equals(setting.GetString(), name, StringComparison.OrdinalIgnoreCase))
            {
                if (!header.TryGetCard($"ZVAL{index}", out var value))
                {
                    return fallback;
                }

                return value.Kind switch
                {
                    FitsValueKind.Logical => value.Logical ? 1 : 0,
                    FitsValueKind.Integer => checked((int)value.Integer),
                    FitsValueKind.Real => checked((int)value.Real),
                    _ => fallback,
                };
            }
        }

        return fallback;
    }

    private static void TileOrigin(int row, int[] counts, int[] tile, int[] origin)
    {
        for (int axis = 0; axis < counts.Length; axis++)
        {
            int coordinate = row % counts[axis];
            row /= counts[axis];
            origin[axis] = coordinate * tile[axis];
        }
    }

    private static void TileSpan(int[] origin, int[] tile, int[] axes, int[] span)
    {
        for (int axis = 0; axis < axes.Length; axis++)
        {
            span[axis] = Math.Min(tile[axis], axes[axis] - origin[axis]);
        }
    }

    private static long ReadInteger(byte[] bytes, int offset, int width)
    {
        long value = 0;
        for (int index = 0; index < width; index++)
        {
            value = (value << 8) | bytes[offset + index];
        }

        return value;
    }

    private static void WriteInteger(Span<byte> destination, long value)
    {
        ulong bits = (ulong)value;
        for (int index = destination.Length - 1; index >= 0; index--)
        {
            destination[index] = (byte)bits;
            bits >>= 8;
        }
    }

    private static float[] BuildDither()
    {
        double seed = 1;
        const double multiplier = 16807;
        const double modulus = 2147483647;
        var values = new float[10000];
        for (int index = 0; index < values.Length; index++)
        {
            double next = multiplier * seed;
            seed = next - (modulus * (int)(next / modulus));
            values[index] = (float)(seed / modulus);
        }

        return values;
    }
}
