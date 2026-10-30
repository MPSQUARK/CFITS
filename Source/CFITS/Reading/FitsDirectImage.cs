using System.Buffers;
using CFITS.Compression;

namespace CFITS;

/// <summary>
/// Decodes an uncompressed image into the caller's array without retaining the file bytes.
/// </summary>
public static class FitsDirectImage
{
    public static bool TryRead<T>(
        Stream stream,
        int rank,
        int hduIndex,
        string? extensionName,
        FitsOpenOptions options,
        out Array? pixels,
        out FitsHeader? header)
        where T : unmanaged =>
        TryRead<T>(stream, rank, hduIndex, extensionName, options, keywords: default, out pixels, out header, materializeHeader: true, validateRemainder: true);

    public static bool TryRead<T>(
        Stream stream,
        int rank,
        int hduIndex,
        string? extensionName,
        FitsOpenOptions options,
        out Array? pixels,
        out FitsHeader? header,
        bool validateRemainder)
        where T : unmanaged =>
        TryRead<T>(stream, rank, hduIndex, extensionName, options, keywords: default, out pixels, out header, materializeHeader: true, validateRemainder);

    public static bool TryRead<T>(
        Stream stream,
        int rank,
        int hduIndex,
        string? extensionName,
        FitsOpenOptions options,
        out Array? pixels)
        where T : unmanaged =>
        TryRead<T>(stream, rank, hduIndex, extensionName, options, keywords: default, out pixels, out _, materializeHeader: false, validateRemainder: true);

    public static bool TryRead<T>(
        Stream stream,
        int rank,
        int hduIndex,
        string? extensionName,
        FitsOpenOptions options,
        out Array? pixels,
        bool validateRemainder)
        where T : unmanaged =>
        TryRead<T>(stream, rank, hduIndex, extensionName, options, keywords: default, out pixels, out _, materializeHeader: false, validateRemainder);

    public static bool TryRead<T>(
        Stream stream,
        int rank,
        int hduIndex,
        string? extensionName,
        FitsOpenOptions options,
        Span<FitsKeywordQuery> keywords,
        out Array? pixels)
        where T : unmanaged =>
        TryRead<T>(stream, rank, hduIndex, extensionName, options, keywords, out pixels, out _, materializeHeader: false, validateRemainder: true);

    public static bool TryRead<T>(
        Stream stream,
        int rank,
        int hduIndex,
        string? extensionName,
        FitsOpenOptions options,
        Span<FitsKeywordQuery> keywords,
        out Array? pixels,
        bool validateRemainder)
        where T : unmanaged =>
        TryRead<T>(stream, rank, hduIndex, extensionName, options, keywords, out pixels, out _, materializeHeader: false, validateRemainder);

    private static bool TryRead<T>(
        Stream stream,
        int rank,
        int hduIndex,
        string? extensionName,
        FitsOpenOptions options,
        Span<FitsKeywordQuery> keywords,
        out Array? pixels,
        out FitsHeader? header,
        bool materializeHeader,
        bool validateRemainder)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(options);
        pixels = null;
        header = null;
        if (options.VerifyChecksum || !stream.CanSeek || rank is < 1 or > 3)
        {
            return false;
        }

        long origin = stream.Position;
        if (!FitsHduStream.TrySelect(stream, hduIndex, extensionName, image: true, out FitsHduStream.Opened opened))
        {
            if (!FitsHduStream.TrySelect(stream, hduIndex, extensionName, image: false, out opened, compressedImage: true))
            {
                return false;
            }

            try
            {
                header = FitsHduStream.CreateHeader(opened.DetachBuffer(), opened.HeaderLength, opened.Index);
                return ReadCompressed<T>(stream, rank, opened, header, options, validateRemainder, out pixels);
            }
            catch
            {
                stream.Position = origin;
                throw;
            }
            finally
            {
                opened.Dispose();
            }
        }

        try
        {
            if (opened.Shape.Groups || opened.Shape.Axes.Length != rank)
            {
                throw new FitsException("Generated image rank does not match the target array.", new FitsLocation(opened.Index));
            }

            int length = 1;
            foreach (int axis in opened.Shape.Axes)
            {
                length = checked(length * axis);
            }

            ReadOnlySpan<byte> cards = opened.Header;
            double scale = HeaderValues.Real(cards, "BSCALE"u8, 1);
            double zero = HeaderValues.Real(cards, "BZERO"u8, 0);
            long? blank = HeaderValues.Integer(cards, "BLANK"u8);
            FitsKeywordQuery.Bind(cards, keywords);
            bool identity = opened.Shape.Bitpix == Bitpix<T>()
                && (options.ScaleMode == FitsScaleMode.Stored || (scale == 1 && zero == 0 && blank is null));
            pixels = Create<T>(opened.Shape.Axes);
            if (identity)
            {
                ReadIdentity<T>(stream, Flat<T>(pixels), length, opened.Shape.Bitpix);
            }
            else
            {
                ReadScaled<T>(stream, pixels, opened, scale, zero, blank, options);
            }

            if (materializeHeader)
            {
                header = FitsHduStream.CreateHeader(opened.DetachBuffer(), opened.HeaderLength, opened.Index);
            }

            FitsHduStream.SkipPadding(stream, opened.Shape);
            if (validateRemainder)
            {
                FitsHduStream.ValidateRemainder(stream);
            }

            return true;
        }
        catch
        {
            stream.Position = origin;
            throw;
        }
        finally
        {
            opened.Dispose();
        }
    }

    private static bool ReadCompressed<T>(
        Stream stream,
        int rank,
        FitsHduStream.Opened opened,
        FitsHeader header,
        FitsOpenOptions options,
        bool validateRemainder,
        out Array? pixels)
        where T : unmanaged
    {
        pixels = null;
        int dataBytes = checked((int)opened.Shape.DataBytes);
        byte[] rentedData = ArrayPool<byte>.Shared.Rent(Math.Max(dataBytes, 1));
        byte[]? rentedImage = null;
        try
        {
            if (dataBytes > 0)
            {
                stream.ReadExactly(rentedData.AsSpan(0, dataBytes));
            }

            FitsTable table = FitsTable.Create(
                header,
                rentedData.AsMemory(0, dataBytes),
                ascii: false,
                opened.Shape,
                options.ScaleMode);
            int imageBytes = TileCompression.UncompressedBytes(header);
            rentedImage = ArrayPool<byte>.Shared.Rent(Math.Max(imageBytes, 1));
            FitsImage image = TileCompression.Decompress(header, table, options.ScaleMode, rentedImage);
            if (image.Axes.Count != rank)
            {
                throw new FitsException("Generated image rank does not match the target array.", new FitsLocation(opened.Index));
            }

            var axes = new int[image.Axes.Count];
            for (int axis = 0; axis < axes.Length; axis++)
            {
                axes[axis] = image.Axes[axis];
            }

            pixels = Create<T>(axes);
            if (typeof(T) == typeof(float) || typeof(T) == typeof(double))
            {
                CopyReal(image, pixels);
            }
            else
            {
                CopyInteger(image, pixels);
            }

            FitsHduStream.SkipPadding(stream, opened.Shape);
            if (validateRemainder)
            {
                FitsHduStream.ValidateRemainder(stream);
            }

            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rentedData);
            if (rentedImage is not null)
            {
                ArrayPool<byte>.Shared.Return(rentedImage);
            }
        }
    }

    private static Array Create<T>(int[] axes)
        where T : unmanaged
    {
        return axes.Length switch
        {
            1 => new T[axes[0]],
            2 => new T[axes[1], axes[0]],
            _ => new T[axes[2], axes[1], axes[0]],
        };
    }

    private static Span<T> Flat<T>(Array pixels)
        where T : unmanaged
    {
        return pixels.Rank switch
        {
            1 => ((T[])pixels).AsSpan(),
            2 => FitsEndian.AsContiguous((T[,])pixels),
            _ => FitsEndian.AsContiguous((T[,,])pixels),
        };
    }

    private static void ReadIdentity<T>(Stream stream, Span<T> destination, int length, int bitpix)
        where T : unmanaged
    {
        int width = Math.Abs(bitpix) / 8;
        if (width == 1)
        {
            stream.ReadExactly(MemoryMarshalBytes(destination));
            return;
        }

        int byteCount = checked(length * width);
        Span<byte> bytes = MemoryMarshalBytes(destination)[..byteCount];
        stream.ReadExactly(bytes);
        Decode(bytes, destination[..length]);
    }

    private static void Decode<T>(ReadOnlySpan<byte> source, Span<T> destination)
        where T : unmanaged
    {
        if (typeof(T) == typeof(short))
        {
            FitsEndian.ReadInt16(source, MemoryMarshalCast<T, short>(destination));
        }
        else if (typeof(T) == typeof(int))
        {
            FitsEndian.ReadInt32(source, MemoryMarshalCast<T, int>(destination));
        }
        else if (typeof(T) == typeof(long))
        {
            FitsEndian.ReadInt64(source, MemoryMarshalCast<T, long>(destination));
        }
        else if (typeof(T) == typeof(float))
        {
            FitsEndian.ReadSingle(source, MemoryMarshalCast<T, float>(destination));
        }
        else if (typeof(T) == typeof(double))
        {
            FitsEndian.ReadDouble(source, MemoryMarshalCast<T, double>(destination));
        }
        else
        {
            throw new FitsException("The image element type is not supported.", new FitsLocation());
        }
    }

    private static void ReadScaled<T>(Stream stream, Array pixels, FitsHduStream.Opened opened, double scale, double zero, long? blank, FitsOpenOptions options)
        where T : unmanaged
    {
        int dataBytes = checked((int)opened.Shape.DataBytes);
        byte[] raw = ArrayPool<byte>.Shared.Rent(Math.Max(dataBytes, 1));
        try
        {
            stream.ReadExactly(raw.AsSpan(0, dataBytes));
            FitsImage image = FitsImage.Create(raw.AsMemory(0, dataBytes), opened.Shape, scale, zero, blank, options.ScaleMode);
            if (typeof(T) == typeof(short))
            {
                CopyInteger(image, pixels);
            }
            else if (typeof(T) == typeof(int))
            {
                CopyInteger(image, pixels);
            }
            else if (typeof(T) == typeof(long))
            {
                CopyInteger(image, pixels);
            }
            else if (typeof(T) == typeof(byte))
            {
                CopyInteger(image, pixels);
            }
            else if (typeof(T) == typeof(float))
            {
                CopyReal(image, pixels);
            }
            else if (typeof(T) == typeof(double))
            {
                CopyReal(image, pixels);
            }
            else
            {
                throw new FitsException("The image element type is not supported.", new FitsLocation());
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(raw);
        }
    }

    private static void CopyInteger(FitsImage image, Array pixels)
    {
        switch (pixels)
        {
            case short[] values:
                image.CopyInteger(values);
                break;
            case short[,] values:
                image.CopyInteger(values);
                break;
            case short[,,] values:
                image.CopyInteger(values);
                break;
            case int[] values:
                image.CopyInteger(values);
                break;
            case int[,] values:
                image.CopyInteger(values);
                break;
            case int[,,] values:
                image.CopyInteger(values);
                break;
            case long[] values:
                image.CopyInteger(values);
                break;
            case long[,] values:
                image.CopyInteger(values);
                break;
            case long[,,] values:
                image.CopyInteger(values);
                break;
            case byte[] values:
                image.CopyInteger(values);
                break;
            case byte[,] values:
                image.CopyInteger(values);
                break;
            case byte[,,] values:
                image.CopyInteger(values);
                break;
            default:
                throw new FitsException("The image element type is not supported.", new FitsLocation());
        }
    }

    private static void CopyReal(FitsImage image, Array pixels)
    {
        switch (pixels)
        {
            case float[] values:
                image.CopyPhysical(values);
                break;
            case float[,] values:
                image.CopyPhysical(values);
                break;
            case float[,,] values:
                image.CopyPhysical(values);
                break;
            case double[] values:
                image.CopyPhysical(values);
                break;
            case double[,] values:
                image.CopyPhysical(values);
                break;
            case double[,,] values:
                image.CopyPhysical(values);
                break;
            default:
                throw new FitsException("The image element type is not supported.", new FitsLocation());
        }
    }

    private static int Bitpix<T>()
        where T : unmanaged
    {
        if (typeof(T) == typeof(byte))
        {
            return 8;
        }

        if (typeof(T) == typeof(short))
        {
            return 16;
        }

        if (typeof(T) == typeof(int))
        {
            return 32;
        }

        if (typeof(T) == typeof(long))
        {
            return 64;
        }

        if (typeof(T) == typeof(float))
        {
            return -32;
        }

        if (typeof(T) == typeof(double))
        {
            return -64;
        }

        return 0;
    }

    private static Span<byte> MemoryMarshalBytes<T>(Span<T> values)
        where T : unmanaged =>
        System.Runtime.InteropServices.MemoryMarshal.AsBytes(values);

    private static Span<TTo> MemoryMarshalCast<TFrom, TTo>(Span<TFrom> values)
        where TFrom : unmanaged
        where TTo : unmanaged =>
        System.Runtime.InteropServices.MemoryMarshal.Cast<TFrom, TTo>(values);
}
