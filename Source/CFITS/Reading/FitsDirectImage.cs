using System.Buffers;
using System.Numerics;
using System.Runtime.InteropServices;
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
        FitsHduStream.Opened opened;
        bool compressedImage;
        bool selected = hduIndex >= 0
            ? FitsHduStream.TrySelectImage(stream, hduIndex, extensionName, out opened, out compressedImage)
            : TrySelectEitherImage(stream, hduIndex, extensionName, out opened, out compressedImage);
        if (!selected)
        {
            return false;
        }

        if (compressedImage)
        {
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

    private static bool TrySelectEitherImage(
        Stream stream,
        int hduIndex,
        string? extensionName,
        out FitsHduStream.Opened opened,
        out bool compressed)
    {
        if (FitsHduStream.TrySelect(stream, hduIndex, extensionName, image: true, out opened))
        {
            compressed = false;
            return true;
        }

        compressed = FitsHduStream.TrySelect(stream, hduIndex, extensionName, image: false, out opened, compressedImage: true);
        return compressed;
    }

    internal static FitsImageLayout Describe(Stream stream, int hduIndex, string? extensionName, FitsOpenOptions options)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(options);
        RequireDirect(stream, options, hduIndex);
        long origin = stream.Position;
        try
        {
            if (!TrySelectImage(stream, hduIndex, extensionName, out FitsHduStream.Opened opened, out bool compressed))
            {
                throw new FitsException("The HDU is not an image.", new FitsLocation(hduIndex));
            }

            try
            {
                return LayoutOf(opened, compressed);
            }
            finally
            {
                opened.Dispose();
            }
        }
        finally
        {
            stream.Position = origin;
        }
    }

    internal static FitsImageLayout Read<T>(Stream stream, Span<T> destination, int hduIndex, string? extensionName, FitsOpenOptions options)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(options);
        EnsureElement<T>();
        RequireDirect(stream, options, hduIndex);
        long origin = stream.Position;
        if (!TrySelectImage(stream, hduIndex, extensionName, out FitsHduStream.Opened opened, out bool compressed))
        {
            throw new FitsException("The HDU is not an image.", new FitsLocation(hduIndex));
        }

        try
        {
            FitsImageLayout layout = LayoutOf(opened, compressed);
            if (destination.Length < layout.PixelCount)
            {
                throw new FitsException(
                    $"The destination has {destination.Length} elements; the image requires {layout.PixelCount}.",
                    new FitsLocation(opened.Index));
            }

            Span<T> pixels = destination[..layout.PixelCount];
            if (compressed)
            {
                FitsHeader header = FitsHduStream.CreateHeader(opened.DetachBuffer(), opened.HeaderLength, opened.Index);
                FillCompressed(stream, opened, header, options, pixels);
            }
            else
            {
                if (opened.Shape.Groups)
                {
                    throw new FitsException("Random groups cannot be read into an image buffer.", new FitsLocation(opened.Index));
                }

                FillPlain(stream, opened, options, pixels);
            }

            FitsHduStream.SkipPadding(stream, opened.Shape);
            FitsHduStream.ValidateRemainder(stream);
            return layout;
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

    private static bool TrySelectImage(
        Stream stream,
        int hduIndex,
        string? extensionName,
        out FitsHduStream.Opened opened,
        out bool compressed) =>
        hduIndex >= 0
            ? FitsHduStream.TrySelectImage(stream, hduIndex, extensionName, out opened, out compressed)
            : TrySelectEitherImage(stream, hduIndex, extensionName, out opened, out compressed);

    private static FitsImageLayout LayoutOf(FitsHduStream.Opened opened, bool compressed)
    {
        if (!compressed)
        {
            int[] axes = opened.Shape.Axes ?? [];
            var copy = new int[axes.Length];
            axes.CopyTo(copy);
            return new FitsImageLayout(copy, opened.Shape.Bitpix, compressed: false);
        }

        ReadOnlySpan<byte> cards = opened.Header;
        int bitpix = checked((int)(HeaderValues.Integer(cards, "ZBITPIX"u8)
            ?? throw new FitsException("A compressed image is missing ZBITPIX.", new FitsLocation(opened.Index, "ZBITPIX"))));
        int rank = checked((int)(HeaderValues.Integer(cards, "ZNAXIS"u8)
            ?? throw new FitsException("A compressed image is missing ZNAXIS.", new FitsLocation(opened.Index, "ZNAXIS"))));
        if (rank < 0)
        {
            throw new FitsException("ZNAXIS must be from 0 through 999.", new FitsLocation(opened.Index, "ZNAXIS"));
        }

        var imageAxes = new int[rank];
        Span<byte> keyword = stackalloc byte[8];
        for (int axis = 0; axis < rank; axis++)
        {
            int keywordLength = WriteAxisKeyword(keyword, axis + 1);
            imageAxes[axis] = checked((int)(HeaderValues.Integer(cards, keyword[..keywordLength])
                ?? throw new FitsException($"A compressed image is missing ZNAXIS{axis + 1}.", new FitsLocation(opened.Index, $"ZNAXIS{axis + 1}"))));
        }

        return new FitsImageLayout(imageAxes, bitpix, compressed: true);
    }

    private static int WriteAxisKeyword(Span<byte> keyword, int axisNumber)
    {
        "ZNAXIS"u8.CopyTo(keyword);
        int length = 6;
        int count = 0;
        int value = axisNumber;
        Span<char> digits = stackalloc char[3];
        do
        {
            digits[count++] = (char)('0' + (value % 10));
            value /= 10;
        }
        while (value > 0);

        if (length + count > keyword.Length)
        {
            throw new FitsException($"ZNAXIS{axisNumber} does not fit in a FITS keyword.", new FitsLocation(Keyword: $"ZNAXIS{axisNumber}"));
        }

        for (int index = count - 1; index >= 0; index--)
        {
            keyword[length++] = (byte)digits[index];
        }

        return length;
    }

    private static void FillPlain<T>(Stream stream, FitsHduStream.Opened opened, FitsOpenOptions options, Span<T> destination)
        where T : unmanaged
    {
        ReadOnlySpan<byte> cards = opened.Header;
        double scale = HeaderValues.Real(cards, "BSCALE"u8, 1);
        double zero = HeaderValues.Real(cards, "BZERO"u8, 0);
        long? blank = HeaderValues.Integer(cards, "BLANK"u8);
        bool identity = opened.Shape.Bitpix == Bitpix<T>()
            && (options.ScaleMode == FitsScaleMode.Stored || (scale == 1 && zero == 0 && blank is null));
        if (identity)
        {
            if (destination.Length > 0)
            {
                ReadIdentity<T>(stream, destination, destination.Length, opened.Shape.Bitpix);
            }

            return;
        }

        int dataBytes = checked((int)opened.Shape.DataBytes);
        byte[] raw = ArrayPool<byte>.Shared.Rent(Math.Max(dataBytes, 1));
        try
        {
            if (dataBytes > 0)
            {
                stream.ReadExactly(raw.AsSpan(0, dataBytes));
            }

            FitsImage image = FitsImage.Create(raw.AsMemory(0, dataBytes), opened.Shape, scale, zero, blank, options.ScaleMode);
            CopyInto(image, destination, options);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(raw);
        }
    }

    private static void FillCompressed<T>(
        Stream stream,
        FitsHduStream.Opened opened,
        FitsHeader header,
        FitsOpenOptions options,
        Span<T> destination)
        where T : unmanaged
    {
        int dataBytes = checked((int)opened.Shape.DataBytes);
        byte[] rentedData = ArrayPool<byte>.Shared.Rent(Math.Max(dataBytes, 1));
        byte[]? rentedImage = null;
        try
        {
            if (dataBytes > 0)
            {
                stream.ReadExactly(rentedData.AsSpan(0, dataBytes));
            }

            FitsTable table = FitsTable.Create(header, rentedData.AsMemory(0, dataBytes), ascii: false, opened.Shape, options.ScaleMode);
            int imageBytes = TileCompression.UncompressedBytes(header);
            rentedImage = ArrayPool<byte>.Shared.Rent(Math.Max(imageBytes, 1));
            FitsImage image = TileCompression.Decompress(header, table, options.ScaleMode, rentedImage);
            CopyInto(image, destination, options);
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

    private static void CopyInto<T>(FitsImage image, Span<T> destination, FitsOpenOptions options)
        where T : unmanaged
    {
        if (typeof(T) == typeof(float))
        {
            image.CopyPhysical(MemoryMarshal.Cast<T, float>(destination));
            return;
        }

        if (typeof(T) == typeof(double))
        {
            image.CopyPhysical(MemoryMarshal.Cast<T, double>(destination));
            return;
        }

        bool identity = options.ScaleMode == FitsScaleMode.Stored || (image.Scale == 1 && image.Zero == 0 && image.Blank is null);
        if (typeof(T) == typeof(byte))
        {
            CopyInteger(image, MemoryMarshal.Cast<T, byte>(destination), identity, 8);
            return;
        }

        if (typeof(T) == typeof(short))
        {
            CopyInteger(image, MemoryMarshal.Cast<T, short>(destination), identity, 16);
            return;
        }

        if (typeof(T) == typeof(int))
        {
            CopyInteger(image, MemoryMarshal.Cast<T, int>(destination), identity, 32);
            return;
        }

        if (typeof(T) == typeof(long))
        {
            CopyInteger(image, MemoryMarshal.Cast<T, long>(destination), identity, 64);
            return;
        }

        throw new FitsException("The image element type is not supported.", new FitsLocation());
    }

    private static void CopyInteger<TNumber>(FitsImage image, Span<TNumber> destination, bool identity, int storedBits)
        where TNumber : unmanaged, INumber<TNumber>
    {
        if (identity && image.Bitpix == storedBits)
        {
            if (typeof(TNumber) == typeof(byte))
            {
                image.CopyInteger(MemoryMarshal.Cast<TNumber, byte>(destination));
                return;
            }

            if (typeof(TNumber) == typeof(short))
            {
                image.CopyInteger(MemoryMarshal.Cast<TNumber, short>(destination));
                return;
            }

            if (typeof(TNumber) == typeof(int))
            {
                image.CopyInteger(MemoryMarshal.Cast<TNumber, int>(destination));
                return;
            }

            image.CopyInteger(MemoryMarshal.Cast<TNumber, long>(destination));
            return;
        }

        for (int index = 0; index < image.Length; index++)
        {
            destination[index] = TNumber.CreateTruncating(image.ReadInteger(index));
        }
    }

    private static void EnsureElement<T>()
        where T : unmanaged
    {
        if (typeof(T) != typeof(byte)
            && typeof(T) != typeof(short)
            && typeof(T) != typeof(int)
            && typeof(T) != typeof(long)
            && typeof(T) != typeof(float)
            && typeof(T) != typeof(double))
        {
            throw new FitsException("The image element type is not supported.", new FitsLocation());
        }
    }

    private static void RequireDirect(Stream stream, FitsOpenOptions options, int hduIndex)
    {
        if (options.VerifyChecksum || !stream.CanSeek)
        {
            throw new FitsException(
                "Reading into a caller buffer requires a seekable stream and does not verify checksums.",
                new FitsLocation(hduIndex));
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
