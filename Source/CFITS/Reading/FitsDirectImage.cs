using System.Buffers;

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
        if (!FitsHduStream.TrySelect(stream, hduIndex, extensionName, image: true, out FitsHduStream.Opened opened, out header))
        {
            return false;
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

            bool identity = opened.Shape.Bitpix == Bitpix<T>()
                && (options.ScaleMode == FitsScaleMode.Stored
                    || (FitsHduStream.Scale(header!, "BSCALE") == 1
                        && FitsHduStream.Zero(header!, "BZERO") == 0
                        && FitsHduStream.Null(header!, "BLANK") is null));
            pixels = Create<T>(opened.Shape.Axes);
            if (identity)
            {
                ReadIdentity<T>(stream, Flat<T>(pixels), length, opened.Shape.Bitpix);
            }
            else
            {
                ReadScaled<T>(stream, pixels, opened, header!, options);
            }

            FitsHduStream.SkipPadding(stream, opened.Shape);
            FitsHduStream.ValidateRemainder(stream);
            return true;
        }
        catch
        {
            stream.Position = origin;
            throw;
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

        byte[] rented = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            int capacity = rented.Length / width * width;
            int filled = 0;
            while (filled < length)
            {
                int count = Math.Min(length - filled, capacity / width);
                Span<byte> source = rented.AsSpan(0, count * width);
                stream.ReadExactly(source);
                Decode(source, destination.Slice(filled, count));
                filled += count;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
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

    private static void ReadScaled<T>(Stream stream, Array pixels, FitsHduStream.Opened opened, FitsHeader header, FitsOpenOptions options)
        where T : unmanaged
    {
        int dataBytes = checked((int)opened.Shape.DataBytes);
        var raw = new byte[dataBytes];
        stream.ReadExactly(raw);
        FitsImage image = FitsImage.Create(header, raw, opened.Shape, options.ScaleMode);
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
