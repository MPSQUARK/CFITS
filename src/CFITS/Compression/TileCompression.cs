using System.IO.Compression;

namespace CFITS;

internal static class TileCompression
{
    private static readonly float[] Dither = BuildDither();

    public static bool IsCompressedImage(FitsHeader header) =>
        header.TryGetCard("ZIMAGE", out var image) && image.Kind == FitsValueKind.Logical && image.Logical;

    public static FitsImage Decompress(FitsHeader header, FitsTable table, FitsScaleMode mode)
    {
        int bitpix = checked((int)header.GetInt64("ZBITPIX"));
        int axesCount = checked((int)header.GetInt64("ZNAXIS"));
        var axes = new int[axesCount];
        var tile = new int[axesCount];
        for (int axis = 0; axis < axesCount; axis++)
        {
            axes[axis] = checked((int)header.GetInt64($"ZNAXIS{axis + 1}"));
            tile[axis] = axis == 0 ? axes[0] : 1;
            if (header.TryGetCard($"ZTILE{axis + 1}", out var tileCard))
            {
                tile[axis] = checked((int)tileCard.GetInt64());
            }
        }

        string algorithm = header.GetString("ZCMPTYPE").Trim().Replace(' ', '_');
        int pixels = 1;
        foreach (int axis in axes)
        {
            pixels = checked(pixels * axis);
        }

        int width = Math.Abs(bitpix) / 8;
        var raw = new byte[pixels * width];
        var counts = new int[axesCount];
        for (int axis = 0; axis < axesCount; axis++)
        {
            counts[axis] = (axes[axis] + tile[axis] - 1) / tile[axis];
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
        for (int row = 0; row < rows; row++)
        {
            var origin = TileOrigin(row, counts, tile);
            var span = TileSpan(origin, tile, axes);
            int tilePixels = 1;
            foreach (int length in span)
            {
                tilePixels *= length;
            }

            byte[] compressed = data.ReadVariableBytes(row);
            bool losslessGzip = compressed.Length == 0 && gzip is not null;
            var decoded = losslessGzip
                ? new TileBytes(Gunzip(gzip!.ReadVariableBytes(row)), width)
                : Decode(algorithm, compressed, tilePixels, bytePix, blockSize, width, smooth);
            double rowScale = losslessGzip ? 1 : scale?.ReadReal(row) ?? 1;
            double rowZero = losslessGzip ? 0 : zero?.ReadReal(row) ?? 0;
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
            return new TileBytes(algorithm == "GZIP_2" ? Unshuffle(decoded, width) : decoded, bytePix);
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
            return new TileBytes(compressed, bytePix);
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
        int cursor = 0;
        Place(axes, origin, span, address =>
        {
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
        });
    }

    private static void Place(int[] axes, int[] origin, int[] span, Action<int> visit)
    {
        var cursor = new int[axes.Length];
        while (true)
        {
            int index = 0;
            int stride = 1;
            for (int axis = 0; axis < axes.Length; axis++)
            {
                index += (origin[axis] + cursor[axis]) * stride;
                stride *= axes[axis];
            }

            visit(index);
            int bump = 0;
            while (bump < axes.Length)
            {
                cursor[bump]++;
                if (cursor[bump] < span[bump])
                {
                    break;
                }

                cursor[bump] = 0;
                bump++;
            }

            if (bump == axes.Length)
            {
                return;
            }
        }
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
        if (width <= 1)
        {
            return shuffled;
        }

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

    private static int[] TileOrigin(int row, int[] counts, int[] tile)
    {
        var origin = new int[counts.Length];
        for (int axis = 0; axis < counts.Length; axis++)
        {
            int coordinate = row % counts[axis];
            row /= counts[axis];
            origin[axis] = coordinate * tile[axis];
        }

        return origin;
    }

    private static int[] TileSpan(int[] origin, int[] tile, int[] axes)
    {
        var span = new int[axes.Length];
        for (int axis = 0; axis < axes.Length; axis++)
        {
            span[axis] = Math.Min(tile[axis], axes[axis] - origin[axis]);
        }

        return span;
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
