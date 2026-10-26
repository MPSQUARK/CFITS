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
        int offset = index * format.Width;
        return bitpix switch
        {
            8 => raw[offset],
            16 => ReadBigEndian<short>(raw, offset),
            32 => ReadBigEndian<int>(raw, offset),
            64 => ReadBigEndian<long>(raw, offset),
            -32 => ReadBigEndian<float>(raw, offset),
            -64 => ReadBigEndian<double>(raw, offset),
            _ => format.Read(raw.Slice(offset, format.Width)),
        };
    }

    public static long ReadInteger(ReadOnlySpan<byte> raw, int bitpix, int index)
    {
        PixelFormat format = GetFormat(bitpix);
        if (format.ReadInteger is not ReadIntegerValue reader)
        {
            throw new FitsException("Stored integers are only defined for integer BITPIX.", new FitsLocation(Keyword: "BITPIX"));
        }

        return ReadIntegerAt(raw, bitpix, index);
    }

    public static void CopyStored(ReadOnlySpan<byte> raw, int bitpix, int length, Span<byte> destination)
    {
        if (destination.Length < length)
        {
            throw new FitsException("The destination is shorter than the image.", new FitsLocation());
        }

        if (bitpix == 8)
        {
            raw[..length].CopyTo(destination);
            return;
        }

        for (int index = 0; index < length; index++)
        {
            destination[index] = (byte)ReadIntegerAt(raw, bitpix, index);
        }
    }

    public static void CopyStored(ReadOnlySpan<byte> raw, int bitpix, int length, Span<short> destination)
    {
        if (destination.Length < length)
        {
            throw new FitsException("The destination is shorter than the image.", new FitsLocation());
        }

        if (bitpix == 16)
        {
            FitsEndian.ReadInt16(raw[..(length * sizeof(short))], destination[..length]);
            return;
        }

        for (int index = 0; index < length; index++)
        {
            destination[index] = (short)ReadIntegerAt(raw, bitpix, index);
        }
    }

    public static void CopyStored(ReadOnlySpan<byte> raw, int bitpix, int length, Span<int> destination)
    {
        if (destination.Length < length)
        {
            throw new FitsException("The destination is shorter than the image.", new FitsLocation());
        }

        if (bitpix == 32)
        {
            FitsEndian.ReadInt32(raw[..(length * sizeof(int))], destination[..length]);
            return;
        }

        for (int index = 0; index < length; index++)
        {
            destination[index] = (int)ReadIntegerAt(raw, bitpix, index);
        }
    }

    public static void CopyStored(ReadOnlySpan<byte> raw, int bitpix, int length, Span<long> destination)
    {
        if (destination.Length < length)
        {
            throw new FitsException("The destination is shorter than the image.", new FitsLocation());
        }

        if (bitpix == 64)
        {
            FitsEndian.ReadInt64(raw[..(length * sizeof(long))], destination[..length]);
            return;
        }

        for (int index = 0; index < length; index++)
        {
            destination[index] = ReadIntegerAt(raw, bitpix, index);
        }
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

    private static long ReadIntegerAt(ReadOnlySpan<byte> raw, int bitpix, int index)
    {
        int offset = index * (bitpix >> 3);
        return bitpix switch
        {
            8 => raw[offset],
            16 => ReadBigEndian<short>(raw, offset),
            32 => ReadBigEndian<int>(raw, offset),
            64 => ReadBigEndian<long>(raw, offset),
            _ => throw new FitsException("Stored integers are only defined for integer BITPIX.", new FitsLocation(Keyword: "BITPIX")),
        };
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
        int offset = 0;
        for (int index = 0; index < values.Length; index++)
        {
            BinaryPrimitives.WriteInt16BigEndian(destination.Slice(offset, sizeof(short)), values[index]);
            offset += sizeof(short);
        }
    }

    public static void WriteBigEndian<T>(ReadOnlySpan<T> values, Span<byte> destination)
        where T : unmanaged
    {
        if (typeof(T) == typeof(byte))
        {
            WriteBigEndian(MemoryMarshal.Cast<T, byte>(values), destination);
        }
        else if (typeof(T) == typeof(short))
        {
            WriteBigEndian(MemoryMarshal.Cast<T, short>(values), destination);
        }
        else if (typeof(T) == typeof(int))
        {
            WriteBigEndian(MemoryMarshal.Cast<T, int>(values), destination);
        }
        else if (typeof(T) == typeof(long))
        {
            WriteBigEndian(MemoryMarshal.Cast<T, long>(values), destination);
        }
        else if (typeof(T) == typeof(float))
        {
            WriteBigEndian(MemoryMarshal.Cast<T, float>(values), destination);
        }
        else if (typeof(T) == typeof(double))
        {
            WriteBigEndian(MemoryMarshal.Cast<T, double>(values), destination);
        }
        else
        {
            throw new FitsException("The image element type is not supported.", new FitsLocation());
        }
    }

    private static void WriteBigEndian(ReadOnlySpan<byte> values, Span<byte> destination)
    {
        EnsureWriteDestination(values.Length, sizeof(byte), destination);
        values.CopyTo(destination);
    }

    private static void WriteBigEndian(ReadOnlySpan<short> values, Span<byte> destination) =>
        FitsEndian.WriteInt16(values, destination);

    private static void WriteBigEndian(ReadOnlySpan<int> values, Span<byte> destination) =>
        FitsEndian.WriteInt32(values, destination);

    private static void WriteBigEndian(ReadOnlySpan<long> values, Span<byte> destination) =>
        FitsEndian.WriteInt64(values, destination);

    private static void WriteBigEndian(ReadOnlySpan<float> values, Span<byte> destination) =>
        FitsEndian.WriteSingle(values, destination);

    private static void WriteBigEndian(ReadOnlySpan<double> values, Span<byte> destination) =>
        FitsEndian.WriteDouble(values, destination);

    private static void EnsureWriteDestination(int count, int width, Span<byte> destination)
    {
        if (destination.Length < checked(count * width))
        {
            throw new FitsException("The destination is shorter than the image.", new FitsLocation());
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
        if (scale == 1 && zero == 0)
        {
            int identityOffset = 0;
            for (int index = 0; index < length; index++)
            {
                destination[index] = double.CreateChecked(ReadBigEndian<T>(raw, identityOffset, width));
                identityOffset += width;
            }

            return;
        }

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
        if (scale == 1 && zero == 0)
        {
            int identityOffset = 0;
            for (int index = 0; index < length; index++)
            {
                destination[index] = (float)double.CreateChecked(ReadBigEndian<T>(raw, identityOffset, width));
                identityOffset += width;
            }

            return;
        }

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
        int row = 0;
        int column = 0;
        for (int index = 0; index < length; index++)
        {
            long stored = long.CreateChecked(ReadBigEndian<T>(raw, offset, width));
            destination[row, column] = blank == stored ? double.NaN : zero + (scale * stored);
            offset += width;
            if (++column == n1)
            {
                column = 0;
                row++;
            }
        }
    }

    private static void CopyInteger3D<T>(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank, double[,,] destination, int n1, int n2)
        where T : unmanaged, IBinaryInteger<T>
    {
        int width = Unsafe.SizeOf<T>();
        int offset = 0;
        int z = 0;
        int row = 0;
        int column = 0;
        for (int index = 0; index < length; index++)
        {
            long stored = long.CreateChecked(ReadBigEndian<T>(raw, offset, width));
            destination[z, row, column] = blank == stored ? double.NaN : zero + (scale * stored);
            offset += width;
            if (++column == n1)
            {
                column = 0;
                if (++row == n2)
                {
                    row = 0;
                    z++;
                }
            }
        }
    }

    private static void CopyInteger2DFloat<T>(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank, float[,] destination, int n1)
        where T : unmanaged, IBinaryInteger<T>
    {
        int width = Unsafe.SizeOf<T>();
        int offset = 0;
        int row = 0;
        int column = 0;
        for (int index = 0; index < length; index++)
        {
            long stored = long.CreateChecked(ReadBigEndian<T>(raw, offset, width));
            destination[row, column] = blank == stored
                ? float.NaN
                : (float)(zero + (scale * stored));
            offset += width;
            if (++column == n1)
            {
                column = 0;
                row++;
            }
        }
    }

    private static void CopyInteger3DFloat<T>(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank, float[,,] destination, int n1, int n2)
        where T : unmanaged, IBinaryInteger<T>
    {
        int width = Unsafe.SizeOf<T>();
        int offset = 0;
        int z = 0;
        int row = 0;
        int column = 0;
        for (int index = 0; index < length; index++)
        {
            long stored = long.CreateChecked(ReadBigEndian<T>(raw, offset, width));
            destination[z, row, column] = blank == stored
                ? float.NaN
                : (float)(zero + (scale * stored));
            offset += width;
            if (++column == n1)
            {
                column = 0;
                if (++row == n2)
                {
                    row = 0;
                    z++;
                }
            }
        }
    }

    private static void CopyReal2D<T>(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank, double[,] destination, int n1)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        int width = Unsafe.SizeOf<T>();
        if (scale == 1 && zero == 0)
        {
            int identityOffset = 0;
            int identityRow = 0;
            int identityColumn = 0;
            for (int index = 0; index < length; index++)
            {
                destination[identityRow, identityColumn] =
                    double.CreateChecked(ReadBigEndian<T>(raw, identityOffset, width));
                identityOffset += width;
                if (++identityColumn == n1)
                {
                    identityColumn = 0;
                    identityRow++;
                }
            }

            return;
        }

        int offset = 0;
        int row = 0;
        int column = 0;
        for (int index = 0; index < length; index++)
        {
            destination[row, column] = zero + (scale * double.CreateChecked(ReadBigEndian<T>(raw, offset, width)));
            offset += width;
            if (++column == n1)
            {
                column = 0;
                row++;
            }
        }
    }

    private static void CopyReal3D<T>(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank, double[,,] destination, int n1, int n2)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        int width = Unsafe.SizeOf<T>();
        if (scale == 1 && zero == 0)
        {
            int identityOffset = 0;
            int identityZ = 0;
            int identityRow = 0;
            int identityColumn = 0;
            for (int index = 0; index < length; index++)
            {
                destination[identityZ, identityRow, identityColumn] =
                    double.CreateChecked(ReadBigEndian<T>(raw, identityOffset, width));
                identityOffset += width;
                if (++identityColumn == n1)
                {
                    identityColumn = 0;
                    if (++identityRow == n2)
                    {
                        identityRow = 0;
                        identityZ++;
                    }
                }
            }

            return;
        }

        int offset = 0;
        int z = 0;
        int row = 0;
        int column = 0;
        for (int index = 0; index < length; index++)
        {
            destination[z, row, column] = zero + (scale * double.CreateChecked(ReadBigEndian<T>(raw, offset, width)));
            offset += width;
            if (++column == n1)
            {
                column = 0;
                if (++row == n2)
                {
                    row = 0;
                    z++;
                }
            }
        }
    }

    private static void CopyReal2DFloat<T>(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank, float[,] destination, int n1)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        int width = Unsafe.SizeOf<T>();
        if (scale == 1 && zero == 0)
        {
            int identityOffset = 0;
            int identityRow = 0;
            int identityColumn = 0;
            for (int index = 0; index < length; index++)
            {
                destination[identityRow, identityColumn] =
                    (float)double.CreateChecked(ReadBigEndian<T>(raw, identityOffset, width));
                identityOffset += width;
                if (++identityColumn == n1)
                {
                    identityColumn = 0;
                    identityRow++;
                }
            }

            return;
        }

        int offset = 0;
        int row = 0;
        int column = 0;
        for (int index = 0; index < length; index++)
        {
            destination[row, column] = (float)(zero + (scale * double.CreateChecked(ReadBigEndian<T>(raw, offset, width))));
            offset += width;
            if (++column == n1)
            {
                column = 0;
                row++;
            }
        }
    }

    private static void CopyReal3DFloat<T>(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank, float[,,] destination, int n1, int n2)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        int width = Unsafe.SizeOf<T>();
        if (scale == 1 && zero == 0)
        {
            int identityOffset = 0;
            int identityZ = 0;
            int identityRow = 0;
            int identityColumn = 0;
            for (int index = 0; index < length; index++)
            {
                destination[identityZ, identityRow, identityColumn] =
                    (float)double.CreateChecked(ReadBigEndian<T>(raw, identityOffset, width));
                identityOffset += width;
                if (++identityColumn == n1)
                {
                    identityColumn = 0;
                    if (++identityRow == n2)
                    {
                        identityRow = 0;
                        identityZ++;
                    }
                }
            }

            return;
        }

        int offset = 0;
        int z = 0;
        int row = 0;
        int column = 0;
        for (int index = 0; index < length; index++)
        {
            destination[z, row, column] = (float)(zero + (scale * double.CreateChecked(ReadBigEndian<T>(raw, offset, width))));
            offset += width;
            if (++column == n1)
            {
                column = 0;
                if (++row == n2)
                {
                    row = 0;
                    z++;
                }
            }
        }
    }

    private static double SumInteger<T>(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank)
        where T : unmanaged, IBinaryInteger<T>
    {
        double sum = 0;
        int width = Unsafe.SizeOf<T>();
        if (scale == 1 && zero == 0 && blank is null)
        {
            if (width == sizeof(byte))
            {
                int byteOffset = 0;
                for (int index = 0; index < length; index++)
                {
                    sum += raw[byteOffset];
                    byteOffset++;
                }

                return sum;
            }

            return SumIdentityIntegers(raw, length, width);
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
        Span<float> floats = stackalloc float[32];
        Span<double> doubles = stackalloc double[16];
        int offset = 0;
        int remaining = length;
        while (remaining > 0)
        {
            int count = Math.Min(remaining, width == sizeof(float) ? floats.Length : doubles.Length);
            if (width == sizeof(float))
            {
                FitsEndian.ReadSingle(raw.Slice(offset, count * width), floats[..count]);
                for (int index = 0; index < count; index++)
                {
                    float stored = floats[index];
                    if (!float.IsNaN(stored))
                    {
                        total.Add(zero + (scale * stored));
                    }
                }
            }
            else
            {
                FitsEndian.ReadDouble(raw.Slice(offset, count * width), doubles[..count]);
                for (int index = 0; index < count; index++)
                {
                    double stored = doubles[index];
                    if (!double.IsNaN(stored))
                    {
                        total.Add(zero + (scale * stored));
                    }
                }
            }

            offset += count * width;
            remaining -= count;
        }

        return total.Sum + total.Compensation;
    }

    private static double SumIdentityIntegers(ReadOnlySpan<byte> raw, int length, int width)
    {
        double sum = 0;
        Span<short> shorts = stackalloc short[32];
        Span<int> ints = stackalloc int[16];
        Span<long> longs = stackalloc long[8];
        int offset = 0;
        int remaining = length;
        while (remaining > 0)
        {
            int count = width == sizeof(short)
                ? Math.Min(remaining, shorts.Length)
                : width == sizeof(int)
                    ? Math.Min(remaining, ints.Length)
                    : Math.Min(remaining, longs.Length);
            if (width == sizeof(short))
            {
                FitsEndian.ReadInt16(raw.Slice(offset, count * width), shorts[..count]);
                for (int index = 0; index < count; index++)
                {
                    sum += shorts[index];
                }
            }
            else if (width == sizeof(int))
            {
                FitsEndian.ReadInt32(raw.Slice(offset, count * width), ints[..count]);
                for (int index = 0; index < count; index++)
                {
                    sum += ints[index];
                }
            }
            else
            {
                FitsEndian.ReadInt64(raw.Slice(offset, count * width), longs[..count]);
                for (int index = 0; index < count; index++)
                {
                    sum += longs[index];
                }
            }

            offset += count * width;
            remaining -= count;
        }

        return sum;
    }

    private static T ReadBigEndian<T>(ReadOnlySpan<byte> raw, int offset) where T : unmanaged =>
        ReadBigEndian<T>(raw, offset, Unsafe.SizeOf<T>());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static T ReadBigEndian<T>(ReadOnlySpan<byte> raw, int offset, int width)
        where T : unmanaged
    {
        if (!BitConverter.IsLittleEndian || width == sizeof(byte))
        {
            return Unsafe.ReadUnaligned<T>(ref Unsafe.Add(ref MemoryMarshal.GetReference(raw), offset));
        }

        if (width == sizeof(short))
        {
            short reversed = BinaryPrimitives.ReverseEndianness(
                Unsafe.ReadUnaligned<short>(ref Unsafe.Add(ref MemoryMarshal.GetReference(raw), offset)));
            return Unsafe.As<short, T>(ref reversed);
        }

        if (width == sizeof(int))
        {
            int reversed = BinaryPrimitives.ReverseEndianness(
                Unsafe.ReadUnaligned<int>(ref Unsafe.Add(ref MemoryMarshal.GetReference(raw), offset)));
            return Unsafe.As<int, T>(ref reversed);
        }

        long reversedLong = BinaryPrimitives.ReverseEndianness(
            Unsafe.ReadUnaligned<long>(ref Unsafe.Add(ref MemoryMarshal.GetReference(raw), offset)));
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
