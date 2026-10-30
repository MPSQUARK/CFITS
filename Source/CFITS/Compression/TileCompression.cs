using System.Buffers;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace CFITS.Compression;

internal static partial class TileCompression
{
    private static readonly float[] Dither = BuildDither();

    public static bool IsCompressedImage(FitsHeader header) =>
        header.TryGetCard("ZIMAGE", out var image) && image.Kind == FitsValueKind.Logical && image.Logical;

    internal static int UncompressedBytes(FitsHeader header)
    {
        int bitpix = checked((int)header.GetInt64("ZBITPIX"));
        int axesCount = checked((int)header.GetInt64("ZNAXIS"));
        int pixels = axesCount == 0 ? 0 : 1;
        for (int axis = 0; axis < axesCount; axis++)
        {
            pixels = checked(pixels * checked((int)header.GetInt64($"ZNAXIS{axis + 1}")));
        }

        return checked(pixels * Pixels.Width(bitpix));
    }

    public static FitsImage Decompress(FitsHeader header, FitsTable table, FitsScaleMode mode) =>
        Decompress(header, table, mode, storage: null);

    internal static FitsImage Decompress(FitsHeader header, FitsTable table, FitsScaleMode mode, byte[]? storage)
    {
        int bitpix = checked((int)header.GetInt64("ZBITPIX"));
        if (!FitsBitpix.IsLegal(bitpix))
        {
            throw new FitsException($"ZBITPIX {bitpix} is not a legal FITS value.", new FitsLocation(Keyword: "ZBITPIX"));
        }

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

        string algorithm = CompressionAlgorithms.Normalize(header.GetString("ZCMPTYPE"));
        if (!CompressionAlgorithms.TryResolveTileImage(algorithm, out CompressionAlgorithmId algorithmId))
        {
            throw new FitsException($"Compression algorithm '{algorithm}' is not supported.", new FitsLocation(Keyword: "ZCMPTYPE"));
        }
        if (algorithmId == CompressionAlgorithmId.Hcompress1 && (axesCount != 2 || bitpix < 0))
        {
            throw new FitsException("HCOMPRESS_1 requires a two-dimensional integer image.", new FitsLocation(Keyword: "ZCMPTYPE"));
        }
        if (algorithmId == CompressionAlgorithmId.Plio1 && bitpix < 0)
        {
            throw new FitsException("PLIO_1 requires an integer image.", new FitsLocation(Keyword: "ZCMPTYPE"));
        }

        int pixels = axes.Length == 0 ? 0 : 1;
        foreach (int axis in axes)
        {
            pixels = checked(pixels * axis);
        }

        int width = Pixels.Width(bitpix);
        bool rice = algorithmId is CompressionAlgorithmId.Rice1 or CompressionAlgorithmId.RiceOne;
        int byteCount = checked(pixels * width);
        if (storage is not null && storage.Length < byteCount)
        {
            throw new FitsException("The decompressed image buffer is too small.", new FitsLocation());
        }

        byte[] raw = storage ?? new byte[byteCount];
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
        FitsColumn? gzip = FindColumn(table, "GZIP_COMPRESSED_DATA");
        FitsColumn? uncompressed = FindColumn(table, "UNCOMPRESSED_DATA");
        if (gzip is not null && uncompressed is not null)
        {
            throw new FitsException("A compressed image cannot contain both GZIP_COMPRESSED_DATA and UNCOMPRESSED_DATA.", new FitsLocation());
        }
        FitsColumn? scale = FindColumn(table, "ZSCALE");
        FitsColumn? zero = FindColumn(table, "ZZERO");
        if ((scale is null) != (zero is null))
        {
            throw new FitsException("ZSCALE and ZZERO must be present together.", new FitsLocation());
        }

        FitsColumn? mask = FindColumn(table, "NULL_PIXEL_MASK");
        string maskAlgorithm = header.TryGetCard("ZMASKCMP", out var maskCard)
            ? CompressionAlgorithms.Normalize(maskCard.GetString() ?? "")
            : "";
        CompressionAlgorithmId maskAlgorithmId = default;
        if (mask is not null)
        {
            if (!CompressionAlgorithms.IsTileMask(maskAlgorithm) || !CompressionAlgorithms.TryResolveTileImage(maskAlgorithm, out maskAlgorithmId))
            {
                throw new FitsException("NULL_PIXEL_MASK requires a supported ZMASKCMP algorithm.", new FitsLocation(Keyword: "ZMASKCMP"));
            }
        }
        ValidateSettings(header);
        int blockSize = Setting(header, "BLOCKSIZE", 32);
        int bytePix = Setting(header, "BYTEPIX", rice ? 4 : width);
        int smooth = Setting(header, "SMOOTH", 0);
        string quantize = header.TryGetCard("ZQUANTIZ", out var quantizeCard) ? quantizeCard.GetString() ?? "NO_DITHER" : "NO_DITHER";
        quantize = CompressionAlgorithms.Normalize(quantize);
        if (!CompressionAlgorithms.IsQuantizeMode(quantize))
        {
            throw new FitsException($"Quantization algorithm '{quantize}' is not supported.", new FitsLocation(Keyword: "ZQUANTIZ"));
        }
        int ditherSeed = 0;
        if (header.TryGetCard("ZDITHER0", out var seedCard))
        {
            long seed = seedCard.GetInt64();
            if (seed is < 1 or > 10000)
            {
                throw new FitsException("ZDITHER0 must be from 1 through 10000.", new FitsLocation(Keyword: "ZDITHER0"));
            }

            ditherSeed = checked((int)seed);
        }
        long? blank = header.TryGetCard("ZBLANK", out var blankCard) && blankCard.Kind == FitsValueKind.Integer ? blankCard.Integer : null;
        if (blank is long marker && !ValidBlank(bitpix, marker))
        {
            throw new FitsException("ZBLANK is outside the compressed image pixel domain.", new FitsLocation(Keyword: "ZBLANK"));
        }
        if (mask is not null && bitpix > 0 && !blank.HasValue)
        {
            throw new FitsException("A masked integer compressed image must define ZBLANK.", new FitsLocation(Keyword: "ZBLANK"));
        }

        int rows = table.RowCount;
        if (rows != expectedTiles)
        {
            throw new FitsException($"Compressed image has {rows} tiles; expected {expectedTiles}.", new FitsLocation());
        }
        ITileDecodeStrategy tileDecoder = TileDecodeStrategies.For(algorithmId);
        ITileDecodeStrategy? maskDecoder = mask is not null ? TileDecodeStrategies.For(maskAlgorithmId) : null;
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

            if (TryWriteWholeGzipTile(algorithmId, rows, pixels, tilePixels, width, bytePix, bitpix, quantize, mask, scale, zero, data, row, raw, byteCount))
            {
                break;
            }

            if (TryWriteContiguousRiceTile(rice, mask, scale, zero, quantize, bytePix, width, bitpix, axes, origin, span, tilePixels, data, row, raw, blockSize))
            {
                continue;
            }

            byte[] compressed = data.ReadVariableBytes(row);
            byte[]? fallback = null;
            bool losslessGzip = CompressionAlgorithms.IsGzip(algorithmId) && gzip is not null && compressed.Length == 0;
            bool losslessRaw = uncompressed is not null && compressed.Length == 0;
            if (losslessGzip)
            {
                fallback = gzip!.ReadVariableBytes(row);
            }
            else if (losslessRaw)
            {
                fallback = uncompressed!.ReadVariableBytes(row);
            }
            else
            {
                if (gzip is not null && gzip.ReadVariableBytes(row).Length != 0)
                {
                    throw new FitsException("A fallback image column must be empty when COMPRESSED_DATA contains a tile.", new FitsLocation());
                }

                if (uncompressed is not null && uncompressed.ReadVariableBytes(row).Length != 0)
                {
                    throw new FitsException("A fallback image column must be empty when COMPRESSED_DATA contains a tile.", new FitsLocation());
                }
            }

            double rowScale = losslessGzip || losslessRaw ? 1 : scale?.ReadReal(row) ?? 1;
            double rowZero = losslessGzip || losslessRaw ? 0 : zero?.ReadReal(row) ?? 0;
            var decoded = losslessGzip
                ? new TileBytes(GunzipExact(fallback!, checked(tilePixels * width)), width)
                : losslessRaw
                ? new TileBytes(RequireLength(fallback!, checked(tilePixels * width)), width)
                : tileDecoder.Decode(compressed, tilePixels, bytePix, blockSize, width, smooth);
            int[] tileOrigin = (int[])origin.Clone();
            WriteTile(raw, axes, origin, span, bitpix, decoded.Bytes, decoded.Width, quantize, ditherSeed, row + 1, rowScale, rowZero, blank);
            if (mask is not null)
            {
                byte[] maskBytes = mask.ReadVariableBytes(row);
                if (maskBytes.Length != 0)
                {
                    TileBytes decodedMask = maskDecoder!.Decode(maskBytes, tilePixels, 1, blockSize, 1, 0);
                    ApplyMask(raw, axes, tileOrigin, span, bitpix, decodedMask.Bytes, blank);
                }
            }
        }

        var shape = new HduShape { Bitpix = bitpix, Axes = axes, Gcount = 1 };
        return FitsImage.Create(ImageHeader(bitpix, axes, blank), raw.AsMemory(0, byteCount), shape, mode);
    }

    private static bool TryWriteContiguousRiceTile(
        bool rice,
        FitsColumn? mask,
        FitsColumn? scale,
        FitsColumn? zero,
        string quantize,
        int bytePix,
        int width,
        int bitpix,
        int[] axes,
        int[] origin,
        int[] span,
        int tilePixels,
        FitsColumn data,
        int row,
        byte[] raw,
        int blockSize)
    {
        if (!rice
            || mask is not null
            || scale is not null
            || zero is not null
            || quantize != "NO_DITHER"
            || bitpix <= 0
            || bytePix != width
            || !TryContiguous(axes, origin, span, out int start, out int count)
            || count != tilePixels)
        {
            return false;
        }

        ReadOnlyMemory<byte> payload = data.ReadVariableMemory(row);
        if (payload.Length == 0)
        {
            return false;
        }

        Rice.WriteBigEndian(payload.Span, raw.AsSpan(checked(start * width), checked(count * width)), tilePixels, bytePix, blockSize);
        return true;
    }

    private static bool TryWriteWholeGzipTile(
        CompressionAlgorithmId algorithmId,
        int rows,
        int pixels,
        int tilePixels,
        int width,
        int bytePix,
        int bitpix,
        string quantize,
        FitsColumn? mask,
        FitsColumn? scale,
        FitsColumn? zero,
        FitsColumn data,
        int row,
        byte[] raw,
        int byteCount)
    {
        if (rows != 1
            || row != 0
            || tilePixels != pixels
            || bytePix != width
            || mask is not null
            || scale is not null
            || zero is not null
            || quantize != "NO_DITHER"
            || !CompressionAlgorithms.IsGzip(algorithmId))
        {
            return false;
        }

        ReadOnlyMemory<byte> payload = data.ReadVariableMemory(row);
        if (payload.Length == 0)
        {
            return false;
        }

        if (algorithmId == CompressionAlgorithmId.Gzip2)
        {
            byte[] shuffled = ArrayPool<byte>.Shared.Rent(byteCount);
            try
            {
                GunzipExact(payload, shuffled.AsSpan(0, byteCount));
                Unshuffle(shuffled.AsSpan(0, byteCount), raw.AsSpan(0, byteCount), width);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(shuffled);
            }
        }
        else
        {
            GunzipExact(payload, raw.AsSpan(0, byteCount));
        }

        return true;
    }

    private readonly record struct TileBytes(byte[] Bytes, int Width);

    private static void ApplyMask(byte[] destination, int[] axes, int[] origin, int[] span, int bitpix, byte[] mask, long? blank)
    {
        int tilePixels = 1;
        foreach (int length in span)
        {
            tilePixels = checked(tilePixels * length);
        }

        if (mask.Length != tilePixels)
        {
            throw new FitsException($"A null-pixel mask has {mask.Length} pixels; expected {tilePixels}.", new FitsLocation());
        }

        int width = Math.Abs(bitpix) / 8;
        int address = 0;
        int stride = 1;
        var coordinates = new int[axes.Length];
        for (int axis = 0; axis < axes.Length; axis++)
        {
            address = checked(address + (origin[axis] * stride));
            stride = checked(stride * axes[axis]);
        }

        for (int tileIndex = 0; tileIndex < tilePixels; tileIndex++)
        {
            if (mask[tileIndex] != 0)
            {
                int offset = checked(address * width);
                if (bitpix > 0)
                {
                    WriteInteger(destination.AsSpan(offset, width), blank!.Value);
                }
                else if (bitpix == -32)
                {
                    System.Buffers.Binary.BinaryPrimitives.WriteSingleBigEndian(destination.AsSpan(offset, 4), float.NaN);
                }
                else
                {
                    System.Buffers.Binary.BinaryPrimitives.WriteDoubleBigEndian(destination.AsSpan(offset, 8), double.NaN);
                }
            }

            int coordinateStride = 1;
            for (int axis = 0; axis < axes.Length; axis++)
            {
                coordinates[axis]++;
                address += coordinateStride;
                if (coordinates[axis] < span[axis])
                {
                    break;
                }

                coordinates[axis] = 0;
                address -= span[axis] * coordinateStride;
                coordinateStride = checked(coordinateStride * axes[axis]);
            }
        }
    }

    private static byte[] Pack(uint[] values, int pixels, int bytePix)
    {
        var bytes = new byte[pixels * bytePix];
        int offset = 0;
        for (int index = 0; index < pixels; index++)
        {
            WriteInteger(bytes.AsSpan(offset, bytePix), values[index]);
            offset += bytePix;
        }

        return bytes;
    }

    private static void WriteTile(byte[] destination, int[] axes, int[] origin, int[] span, int bitpix, byte[] decoded, int bytePix, string quantize, int seed, int tileNumber, double scale, double zero, long? blank)
    {
        bool floating = bitpix < 0;
        bool dither = quantize is "SUBTRACTIVE_DITHER_1" or "SUBTRACTIVE_DITHER_2";
        bool restoreZero = quantize == "SUBTRACTIVE_DITHER_2";
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
        int address = 0;
        int stride = 1;
        for (int axis = 0; axis < axes.Length; axis++)
        {
            address += origin[axis] * stride;
            origin[axis] = 0;
            stride *= axes[axis];
        }
        int destinationOffset = address * width;

        for (int tileIndex = 0; tileIndex < tilePixels; tileIndex++)
        {
            double value;
            if (!floating)
            {
                value = ReadInteger(decoded, cursor, bytePix, bitpix != 8);
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
                long quantized = ReadInteger(decoded, cursor, bytePix, signed: true);
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
                        sequence++;
                        if (sequence == 10000)
                        {
                            sequence = 0;
                        }

                        ditherIndex = (int)(Dither[sequence] * 500);
                    }
                }
                else
                {
                    value = (quantized * scale) + zero;
                }
            }

            if (bitpix > 0)
            {
                WriteInteger(destination.AsSpan(destinationOffset, width), (long)value);
            }
            else if (bitpix == -32)
            {
                System.Buffers.Binary.BinaryPrimitives.WriteSingleBigEndian(destination.AsSpan(destinationOffset, 4), (float)value);
            }
            else
            {
                System.Buffers.Binary.BinaryPrimitives.WriteDoubleBigEndian(destination.AsSpan(destinationOffset, 8), value);
            }

            int coordinateStride = 1;
            for (int axis = 0; axis < axes.Length; axis++)
            {
                origin[axis]++;
                int byteStride = coordinateStride * width;
                destinationOffset += byteStride;
                if (origin[axis] < span[axis])
                {
                    break;
                }

                origin[axis] = 0;
                destinationOffset -= span[axis] * byteStride;
                coordinateStride *= axes[axis];
            }
        }
    }

    private static void GunzipExact(ReadOnlyMemory<byte> compressed, Span<byte> output)
    {
        if (!MemoryMarshal.TryGetArray(compressed, out ArraySegment<byte> segment) || segment.Array is null)
        {
            GunzipExact(new ArraySegment<byte>(compressed.ToArray()), output);
            return;
        }

        GunzipExact(segment, output);
    }

    private static void GunzipExact(ArraySegment<byte> compressed, Span<byte> output)
    {
        try
        {
            using var input = new MemoryStream(compressed.Array!, compressed.Offset, compressed.Count, writable: false);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            int read = 0;
            while (read < output.Length)
            {
                int count = gzip.Read(output[read..]);
                if (count == 0)
                {
                    throw new FitsException($"Decoded image tile has {read} bytes; expected {output.Length}.", new FitsLocation());
                }

                read += count;
            }

            if (gzip.ReadByte() >= 0)
            {
                throw new FitsException($"Decoded image tile is longer than the expected {output.Length} bytes.", new FitsLocation());
            }
        }
        catch (InvalidDataException exception)
        {
            throw new FitsException("A compressed image tile is not a valid GZIP stream.", new FitsLocation(), exception);
        }
    }

    private static void Unshuffle(ReadOnlySpan<byte> shuffled, Span<byte> destination, int width)
    {
        if (destination.Length != shuffled.Length)
        {
            throw new FitsException("A shuffled tile does not match the image buffer.", new FitsLocation());
        }

        if (width <= 1)
        {
            shuffled.CopyTo(destination);
            return;
        }

        if (shuffled.Length % width != 0)
        {
            throw new FitsException("A shuffled tile length is not divisible by its element width.", new FitsLocation());
        }

        int count = shuffled.Length / width;
        int sourceOffset = 0;
        int destinationOffset = 0;
        for (int item = 0; item < count; item++)
        {
            for (int byteIndex = 0; byteIndex < width; byteIndex++)
            {
                destination[destinationOffset + byteIndex] = shuffled[sourceOffset];
                sourceOffset += count;
            }

            destinationOffset += width;
            sourceOffset = item + 1;
        }
    }

    private static byte[] Gunzip(byte[] compressed)
    {
        try
        {
            using var output = new MemoryStream();
            using (var input = new MemoryStream(compressed, writable: false))
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            {
                gzip.CopyTo(output);
            }

            return output.ToArray();
        }
        catch (InvalidDataException exception)
        {
            throw new FitsException("A compressed image tile is not a valid GZIP stream.", new FitsLocation(), exception);
        }
    }

    private static byte[] GunzipExact(byte[] compressed, int length)
    {
        try
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
                    throw new FitsException($"Decoded image tile has {read} bytes; expected {length}.", new FitsLocation());
                }

                read += count;
            }

            if (gzip.ReadByte() >= 0)
            {
                throw new FitsException($"Decoded image tile is longer than the expected {length} bytes.", new FitsLocation());
            }

            return output;
        }
        catch (InvalidDataException exception)
        {
            throw new FitsException("A compressed image tile is not a valid GZIP stream.", new FitsLocation(), exception);
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
        if (width == 4)
        {
            int destinationOffset = 0;
            for (int item = 0; item < count; item++)
            {
                int sourceOffset = item;
                destination[destinationOffset++] = shuffled[sourceOffset];
                destination[destinationOffset++] = shuffled[sourceOffset + count];
                destination[destinationOffset++] = shuffled[sourceOffset + (count * 2)];
                destination[destinationOffset++] = shuffled[sourceOffset + (count * 3)];
            }

            return;
        }

        for (int item = 0; item < count; item++)
        {
            int sourceOffset = item;
            int destinationOffset = item * width;
            for (int byteIndex = 0; byteIndex < width; byteIndex++)
            {
                destination[destinationOffset + byteIndex] = shuffled[sourceOffset];
                sourceOffset += count;
            }
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
        int destinationOffset = 0;
        for (int item = 0; item < count; item++)
        {
            for (int byteIndex = 0; byteIndex < width; byteIndex++)
            {
                bytes[destinationOffset + byteIndex] = shuffled[sourceOffset];
                sourceOffset += count;
            }

            destinationOffset += width;
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

    private static void ValidateSettings(FitsHeader header)
    {
        for (int index = 1; index < 1000; index++)
        {
            bool hasName = header.TryGetCard($"ZNAME{index}", out var name);
            bool hasValue = header.TryGetCard($"ZVAL{index}", out var value);
            if (!hasName && !hasValue)
            {
                return;
            }

            if (!hasName || !hasValue)
            {
                throw new FitsException($"ZNAME{index} and ZVAL{index} must be present together.", new FitsLocation(Keyword: hasName ? $"ZVAL{index}" : $"ZNAME{index}"));
            }

            string setting = name.GetString()?.Trim().ToUpperInvariant() ?? "";
            if (setting is not ("BLOCKSIZE" or "BYTEPIX" or "NOISEBIT" or "SCALE" or "SMOOTH"))
            {
                throw new FitsException($"Compression setting '{setting}' is not supported.", new FitsLocation(Keyword: $"ZNAME{index}"));
            }

            if (value.Kind is not (FitsValueKind.Logical or FitsValueKind.Integer or FitsValueKind.Real))
            {
                throw new FitsException($"ZVAL{index} must be numeric or logical.", new FitsLocation(Keyword: $"ZVAL{index}"));
            }

            double numeric = value.Kind == FitsValueKind.Logical ? (value.Logical ? 1 : 0) : value.GetDouble();
            if (!double.IsFinite(numeric) || numeric < 0 || numeric > int.MaxValue || Math.Truncate(numeric) != numeric)
            {
                throw new FitsException($"ZVAL{index} must be a non-negative integer.", new FitsLocation(Keyword: $"ZVAL{index}"));
            }

            if ((setting is "BLOCKSIZE" or "BYTEPIX" or "NOISEBIT") && numeric == 0)
            {
                throw new FitsException($"ZVAL{index} must be positive for {setting}.", new FitsLocation(Keyword: $"ZVAL{index}"));
            }
        }

        throw new FitsException("Compression settings exceed the supported count.", new FitsLocation(Keyword: "ZNAME"));
    }

    private static FitsHeader ImageHeader(int bitpix, int[] axes, long? blank)
    {
        var builder = FitsHeader.Builder()
            .Integer("BITPIX", bitpix)
            .Integer("NAXIS", axes.Length);
        for (int axis = 0; axis < axes.Length; axis++)
        {
            builder.Integer($"NAXIS{axis + 1}", axes[axis]);
        }

        if (bitpix > 0 && blank is long marker)
        {
            builder.Integer("BLANK", marker);
        }

        return builder.Build();
    }

    private static bool ValidBlank(int bitpix, long value)
    {
        if (bitpix < 0)
        {
            return true;
        }

        if (bitpix == 8)
        {
            return value is >= 0 and <= byte.MaxValue;
        }

        if (bitpix == 64)
        {
            return true;
        }

        long minimum = -(1L << (bitpix - 1));
        long maximum = (1L << (bitpix - 1)) - 1;
        return value >= minimum && value <= maximum;
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

    private static long ReadInteger(byte[] bytes, int offset, int width, bool signed)
    {
        long value = 0;
        for (int index = 0; index < width; index++)
        {
            value = (value << 8) | bytes[offset + index];
        }

        if (signed)
        {
            int shift = (sizeof(long) - width) * 8;
            value = (value << shift) >> shift;
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

    private static FitsColumn? FindColumn(FitsTable table, string name)
    {
        IReadOnlyList<FitsColumn> columns = table.Columns;
        for (int index = 0; index < columns.Count; index++)
        {
            if (columns[index].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return columns[index];
            }
        }

        return null;
    }
}
