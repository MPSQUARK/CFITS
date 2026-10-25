using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CFITS.Data;

internal static class Pixels
{
    private delegate double ReadValue(ReadOnlySpan<byte> source);
    private delegate long ReadIntegerValue(ReadOnlySpan<byte> source);
    private delegate void CopyValues(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank, Span<double> destination);
    private delegate void CopyFloatValues(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank, Span<float> destination);
    private delegate void CopyDouble2DValues(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank, double[,] destination, int n1);
    private delegate void CopyDouble3DValues(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank, double[,,] destination, int n1, int n2);
    private delegate void CopyFloat2DValues(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank, float[,] destination, int n1);
    private delegate void CopyFloat3DValues(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank, float[,,] destination, int n1, int n2);
    private delegate double SumValues(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank);
    private delegate void WriteValue(Span<byte> destination, double value);

    private sealed class PixelFormat
    {
        public required int Width { get; init; }
        public required ReadValue Read { get; init; }
        public ReadIntegerValue? ReadInteger { get; init; }
        public required CopyValues Copy { get; init; }
        public required CopyFloatValues CopyFloat { get; init; }
        public required CopyDouble2DValues CopyDouble2D { get; init; }
        public required CopyDouble3DValues CopyDouble3D { get; init; }
        public required CopyFloat2DValues CopyFloat2D { get; init; }
        public required CopyFloat3DValues CopyFloat3D { get; init; }
        public required SumValues Sum { get; init; }
        public required WriteValue Write { get; init; }
    }

    private static readonly PixelFormat?[] Formats = CreateFormats();

    public static int Width(int bitpix) => GetFormat(bitpix).Width;

    public static double Read(ReadOnlySpan<byte> raw, int bitpix, int index)
    {
        PixelFormat format = GetFormat(bitpix);
        return format.Read(raw.Slice(index * format.Width, format.Width));
    }

    public static long ReadInteger(ReadOnlySpan<byte> raw, int bitpix, int index)
    {
        PixelFormat format = GetFormat(bitpix);
        if (format.ReadInteger is not ReadIntegerValue reader)
        {
            throw new FitsException("Stored integers are only defined for integer BITPIX.", new FitsLocation(Keyword: "BITPIX"));
        }

        return reader(raw.Slice(index * format.Width, format.Width));
    }

    public static void Write(Span<byte> raw, int bitpix, int index, double stored)
    {
        PixelFormat format = GetFormat(bitpix);
        format.Write(raw.Slice(index * format.Width, format.Width), stored);
    }

    public static bool IsExactInteger(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value) && value == Math.Truncate(value);

    public static Int128 ToInt128(double value)
    {
        if (!IsExactInteger(value))
        {
            throw new FitsException("The offset is not an integer.", new FitsLocation());
        }

        // long.MaxValue is not an exact double, so compare against 2^63 itself.
        if (value >= -9223372036854775808d && value < 9223372036854775808d)
        {
            return (long)value;
        }

        return (Int128)value;
    }

    public static double Physical(double stored, long storedInteger, bool integer, long? blank, double scale, double zero)
    {
        if (integer && blank is long marker && storedInteger == marker)
        {
            return double.NaN;
        }

        return zero + (scale * stored);
    }

    public static void Copy(ReadOnlySpan<byte> raw, int bitpix, int length, double scale, double zero, long? blank, Span<double> destination)
    {
        if (destination.Length < length)
        {
            throw new FitsException("The destination is shorter than the image.", new FitsLocation());
        }

        GetFormat(bitpix).Copy(raw, length, scale, zero, blank, destination);
    }

    public static void Copy(ReadOnlySpan<byte> raw, int bitpix, int length, double scale, double zero, long? blank, Span<float> destination)
    {
        if (destination.Length < length)
        {
            throw new FitsException("The destination is shorter than the image.", new FitsLocation());
        }

        GetFormat(bitpix).CopyFloat(raw, length, scale, zero, blank, destination);
    }

    public static void Copy(ReadOnlySpan<byte> raw, int bitpix, int length, double scale, double zero, long? blank, double[,] destination, int n1)
    {
        GetFormat(bitpix).CopyDouble2D(raw, length, scale, zero, blank, destination, n1);
    }

    public static void Copy(ReadOnlySpan<byte> raw, int bitpix, int length, double scale, double zero, long? blank, double[,,] destination, int n1, int n2)
    {
        GetFormat(bitpix).CopyDouble3D(raw, length, scale, zero, blank, destination, n1, n2);
    }

    public static void Copy(ReadOnlySpan<byte> raw, int bitpix, int length, double scale, double zero, long? blank, float[,] destination, int n1)
    {
        GetFormat(bitpix).CopyFloat2D(raw, length, scale, zero, blank, destination, n1);
    }

    public static void Copy(ReadOnlySpan<byte> raw, int bitpix, int length, double scale, double zero, long? blank, float[,,] destination, int n1, int n2)
    {
        GetFormat(bitpix).CopyFloat3D(raw, length, scale, zero, blank, destination, n1, n2);
    }

    public static double Sum(ReadOnlySpan<byte> raw, int bitpix, int length, double scale, double zero, long? blank)
    {
        return GetFormat(bitpix).Sum(raw, length, scale, zero, blank);
    }

    private static PixelFormat?[] CreateFormats()
    {
        var formats = new PixelFormat?[129];
        formats[8 + 64] = CreateInteger<byte>(WriteByte);
        formats[16 + 64] = CreateInteger<short>(WriteShort);
        formats[32 + 64] = CreateInteger<int>(WriteInt);
        formats[64 + 64] = CreateInteger<long>(WriteLong);
        formats[-32 + 64] = CreateReal<float>(WriteFloat);
        formats[-64 + 64] = CreateReal<double>(WriteDouble);
        return formats;
    }

    private static PixelFormat GetFormat(int bitpix)
    {
        int index = bitpix + 64;
        if ((uint)index < (uint)Formats.Length && Formats[index] is { } format)
        {
            return format;
        }

        throw new FitsException($"BITPIX {bitpix} is not a legal FITS value.", new FitsLocation(Keyword: "BITPIX"));
    }

    private static int WidthFromBitpix(int bitpix)
    {
        if (bitpix is not (8 or 16 or 32 or 64 or -32 or -64))
        {
            throw new FitsException($"BITPIX {bitpix} is not a legal FITS value.", new FitsLocation(Keyword: "BITPIX"));
        }

        return (bitpix < 0 ? -bitpix : bitpix) >> 3;
    }

    private static PixelFormat CreateInteger<T>(WriteValue write)
        where T : unmanaged, IBinaryInteger<T>
    {
        int width = WidthFromBitpix(Unsafe.SizeOf<T>() << 3);
        return new PixelFormat
        {
            Width = width,
            Read = source => double.CreateChecked(ReadBigEndian<T>(source, 0)),
            ReadInteger = source => long.CreateChecked(ReadBigEndian<T>(source, 0)),
            Copy = CopyInteger<T>,
            CopyFloat = CopyInteger<T>,
            CopyDouble2D = CopyInteger2D<T>,
            CopyDouble3D = CopyInteger3D<T>,
            CopyFloat2D = CopyInteger2DFloat<T>,
            CopyFloat3D = CopyInteger3DFloat<T>,
            Sum = SumInteger<T>,
            Write = write,
        };
    }

    private static PixelFormat CreateReal<T>(WriteValue write)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        int width = WidthFromBitpix(Unsafe.SizeOf<T>() << 3);
        return new PixelFormat
        {
            Width = width,
            Read = source => double.CreateChecked(ReadBigEndian<T>(source, 0)),
            Copy = CopyReal<T>,
            CopyFloat = CopyReal<T>,
            CopyDouble2D = CopyReal2D<T>,
            CopyDouble3D = CopyReal3D<T>,
            CopyFloat2D = CopyReal2DFloat<T>,
            CopyFloat3D = CopyReal3DFloat<T>,
            Sum = SumReal<T>,
            Write = write,
        };
    }

    private static void WriteByte(Span<byte> destination, double value) => destination[0] = (byte)value;

    private static void WriteShort(Span<byte> destination, double value) =>
        BinaryPrimitives.WriteInt16BigEndian(destination, (short)value);

    private static void WriteInt(Span<byte> destination, double value) =>
        BinaryPrimitives.WriteInt32BigEndian(destination, (int)value);

    private static void WriteLong(Span<byte> destination, double value) =>
        BinaryPrimitives.WriteInt64BigEndian(destination, (long)value);

    private static void WriteFloat(Span<byte> destination, double value) =>
        BinaryPrimitives.WriteSingleBigEndian(destination, (float)value);

    private static void WriteDouble(Span<byte> destination, double value) =>
        BinaryPrimitives.WriteDoubleBigEndian(destination, value);

    public static void WriteInt16BigEndian(ReadOnlySpan<short> values, Span<byte> destination)
    {
        for (int index = 0; index < values.Length; index++)
        {
            BinaryPrimitives.WriteInt16BigEndian(destination.Slice(index * sizeof(short), sizeof(short)), values[index]);
        }
    }

    private static void CopyInteger<T>(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank, Span<double> destination)
        where T : unmanaged, IBinaryInteger<T>
    {
        int width = Unsafe.SizeOf<T>();
        if (scale == 1 && zero == 0 && blank is null)
        {
            int offset = 0;
            for (int index = 0; index < length; index++)
            {
                destination[index] = double.CreateChecked(ReadBigEndian<T>(raw, offset, width));
                offset += width;
            }

            return;
        }

        if (blank is null)
        {
            int offset = 0;
            for (int index = 0; index < length; index++)
            {
                long stored = long.CreateChecked(ReadBigEndian<T>(raw, offset, width));
                destination[index] = zero + (scale * stored);
                offset += width;
            }

            return;
        }

        int position = 0;
        for (int index = 0; index < length; index++)
        {
            long stored = long.CreateChecked(ReadBigEndian<T>(raw, position, width));
            destination[index] = blank == stored ? double.NaN : zero + (scale * stored);
            position += width;
        }
    }

    private static void CopyInteger<T>(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank, Span<float> destination)
        where T : unmanaged, IBinaryInteger<T>
    {
        int width = Unsafe.SizeOf<T>();
        if (scale == 1 && zero == 0 && blank is null)
        {
            int offset = 0;
            for (int index = 0; index < length; index++)
            {
                destination[index] = (float)double.CreateChecked(ReadBigEndian<T>(raw, offset, width));
                offset += width;
            }

            return;
        }

        if (blank is null)
        {
            int offset = 0;
            for (int index = 0; index < length; index++)
            {
                long stored = long.CreateChecked(ReadBigEndian<T>(raw, offset, width));
                destination[index] = (float)(zero + (scale * stored));
                offset += width;
            }

            return;
        }

        int position = 0;
        for (int index = 0; index < length; index++)
        {
            long stored = long.CreateChecked(ReadBigEndian<T>(raw, position, width));
            destination[index] = blank == stored ? float.NaN : (float)(zero + (scale * stored));
            position += width;
        }
    }

    private static void CopyReal<T>(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank, Span<double> destination)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        int width = Unsafe.SizeOf<T>();
        int offset = 0;
        for (int index = 0; index < length; index++)
        {
            destination[index] = zero + (scale * double.CreateChecked(ReadBigEndian<T>(raw, offset, width)));
            offset += width;
        }
    }

    private static void CopyReal<T>(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank, Span<float> destination)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        int width = Unsafe.SizeOf<T>();
        int offset = 0;
        for (int index = 0; index < length; index++)
        {
            destination[index] = (float)(zero + (scale * double.CreateChecked(ReadBigEndian<T>(raw, offset, width))));
            offset += width;
        }
    }

    private static void CopyInteger2D<T>(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank, double[,] destination, int n1)
        where T : unmanaged, IBinaryInteger<T>
    {
        int width = Unsafe.SizeOf<T>();
        int offset = 0;
        for (int index = 0; index < length; index++)
        {
            long stored = long.CreateChecked(ReadBigEndian<T>(raw, offset, width));
            destination[index / n1, index % n1] = blank == stored ? double.NaN : zero + (scale * stored);
            offset += width;
        }
    }

    private static void CopyInteger3D<T>(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank, double[,,] destination, int n1, int n2)
        where T : unmanaged, IBinaryInteger<T>
    {
        int width = Unsafe.SizeOf<T>();
        int plane = n1 * n2;
        int offset = 0;
        for (int index = 0; index < length; index++)
        {
            long stored = long.CreateChecked(ReadBigEndian<T>(raw, offset, width));
            int z = index / plane;
            int remainder = index - (z * plane);
            destination[z, remainder / n1, remainder % n1] = blank == stored ? double.NaN : zero + (scale * stored);
            offset += width;
        }
    }

    private static void CopyInteger2DFloat<T>(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank, float[,] destination, int n1)
        where T : unmanaged, IBinaryInteger<T>
    {
        int width = Unsafe.SizeOf<T>();
        int offset = 0;
        for (int index = 0; index < length; index++)
        {
            long stored = long.CreateChecked(ReadBigEndian<T>(raw, offset, width));
            destination[index / n1, index % n1] = blank == stored
                ? float.NaN
                : (float)(zero + (scale * stored));
            offset += width;
        }
    }

    private static void CopyInteger3DFloat<T>(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank, float[,,] destination, int n1, int n2)
        where T : unmanaged, IBinaryInteger<T>
    {
        int width = Unsafe.SizeOf<T>();
        int plane = n1 * n2;
        int offset = 0;
        for (int index = 0; index < length; index++)
        {
            int z = index / plane;
            int remainder = index - (z * plane);
            long stored = long.CreateChecked(ReadBigEndian<T>(raw, offset, width));
            destination[z, remainder / n1, remainder % n1] = blank == stored
                ? float.NaN
                : (float)(zero + (scale * stored));
            offset += width;
        }
    }

    private static void CopyReal2D<T>(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank, double[,] destination, int n1)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        int width = Unsafe.SizeOf<T>();
        int offset = 0;
        for (int index = 0; index < length; index++)
        {
            destination[index / n1, index % n1] = zero + (scale * double.CreateChecked(ReadBigEndian<T>(raw, offset, width)));
            offset += width;
        }
    }

    private static void CopyReal3D<T>(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank, double[,,] destination, int n1, int n2)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        int width = Unsafe.SizeOf<T>();
        int plane = n1 * n2;
        int offset = 0;
        for (int index = 0; index < length; index++)
        {
            int z = index / plane;
            int remainder = index - (z * plane);
            destination[z, remainder / n1, remainder % n1] = zero + (scale * double.CreateChecked(ReadBigEndian<T>(raw, offset, width)));
            offset += width;
        }
    }

    private static void CopyReal2DFloat<T>(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank, float[,] destination, int n1)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        int width = Unsafe.SizeOf<T>();
        int offset = 0;
        for (int index = 0; index < length; index++)
        {
            destination[index / n1, index % n1] = (float)(zero + (scale * double.CreateChecked(ReadBigEndian<T>(raw, offset, width))));
            offset += width;
        }
    }

    private static void CopyReal3DFloat<T>(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank, float[,,] destination, int n1, int n2)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        int width = Unsafe.SizeOf<T>();
        int plane = n1 * n2;
        int offset = 0;
        for (int index = 0; index < length; index++)
        {
            int z = index / plane;
            int remainder = index - (z * plane);
            destination[z, remainder / n1, remainder % n1] = (float)(zero + (scale * double.CreateChecked(ReadBigEndian<T>(raw, offset, width))));
            offset += width;
        }
    }

    private static double SumInteger<T>(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank)
        where T : unmanaged, IBinaryInteger<T>
    {
        double sum = 0;
        int width = Unsafe.SizeOf<T>();
        if (scale == 1 && zero == 0 && blank is null)
        {
            int offset = 0;
            for (int index = 0; index < length; index++)
            {
                sum += double.CreateChecked(ReadBigEndian<T>(raw, offset, width));
                offset += width;
            }

            return sum;
        }

        if (blank is null)
        {
            int offset = 0;
            for (int index = 0; index < length; index++)
            {
                long stored = long.CreateChecked(ReadBigEndian<T>(raw, offset, width));
                sum += zero + (scale * stored);
                offset += width;
            }

            return sum;
        }

        int position = 0;
        for (int index = 0; index < length; index++)
        {
            long stored = long.CreateChecked(ReadBigEndian<T>(raw, position, width));
            if (blank != stored)
            {
                sum += zero + (scale * stored);
            }

            position += width;
        }

        return sum;
    }

    private static double SumReal<T>(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        var total = new Float64NeumaierState();
        int width = Unsafe.SizeOf<T>();
        int offset = 0;
        for (int index = 0; index < length; index++)
        {
            T stored = ReadBigEndian<T>(raw, offset, width);
            if (!T.IsNaN(stored))
            {
                total.Add(zero + (scale * double.CreateChecked(stored)));
            }

            offset += width;
        }

        return total.Sum + total.Compensation;
    }

    private static T ReadBigEndian<T>(ReadOnlySpan<byte> raw, int offset) where T : unmanaged =>
        ReadBigEndian<T>(raw, offset, Unsafe.SizeOf<T>());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static T ReadBigEndian<T>(ReadOnlySpan<byte> raw, int offset, int width)
        where T : unmanaged
    {
        ReadOnlySpan<byte> source = raw.Slice(offset, width);
        if (!BitConverter.IsLittleEndian || width == sizeof(byte))
        {
            return MemoryMarshal.Read<T>(source);
        }

        if (width == sizeof(short))
        {
            short reversed = BinaryPrimitives.ReverseEndianness(MemoryMarshal.Read<short>(source));
            return Unsafe.As<short, T>(ref reversed);
        }

        if (width == sizeof(int))
        {
            int reversed = BinaryPrimitives.ReverseEndianness(MemoryMarshal.Read<int>(source));
            return Unsafe.As<int, T>(ref reversed);
        }

        long reversedLong = BinaryPrimitives.ReverseEndianness(MemoryMarshal.Read<long>(source));
        return Unsafe.As<long, T>(ref reversedLong);
    }

    /// <summary>Scalar float64 Neumaier accumulator, matching the BAVCL compensated sum.</summary>
    private struct Float64NeumaierState
    {
        public double Sum;
        public double Compensation;

        public void Add(double input)
        {
            double total = Sum + input;
            if (Math.Abs(Sum) >= Math.Abs(input))
            {
                Compensation += (Sum - total) + input;
            }
            else
            {
                Compensation += (input - total) + Sum;
            }

            Sum = total;
        }
    }

}
