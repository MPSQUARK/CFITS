using System.Buffers.Binary;

namespace CFITS;

internal static class Pixels
{
    public static int Width(int bitpix) => Math.Abs(bitpix) / 8;

    public static double Read(ReadOnlySpan<byte> raw, int bitpix, int index)
    {
        int width = Width(bitpix);
        var slice = raw.Slice(index * width, width);
        return bitpix switch
        {
            8 => slice[0],
            16 => BinaryPrimitives.ReadInt16BigEndian(slice),
            32 => BinaryPrimitives.ReadInt32BigEndian(slice),
            64 => BinaryPrimitives.ReadInt64BigEndian(slice),
            -32 => BinaryPrimitives.ReadSingleBigEndian(slice),
            -64 => BinaryPrimitives.ReadDoubleBigEndian(slice),
            _ => throw new FitsException($"BITPIX {bitpix} is not a legal FITS value.", new FitsLocation(Keyword: "BITPIX")),
        };
    }

    public static long ReadInteger(ReadOnlySpan<byte> raw, int bitpix, int index)
    {
        int width = Width(bitpix);
        var slice = raw.Slice(index * width, width);
        return bitpix switch
        {
            8 => slice[0],
            16 => BinaryPrimitives.ReadInt16BigEndian(slice),
            32 => BinaryPrimitives.ReadInt32BigEndian(slice),
            64 => BinaryPrimitives.ReadInt64BigEndian(slice),
            _ => throw new FitsException("Stored integers are only defined for integer BITPIX.", new FitsLocation(Keyword: "BITPIX")),
        };
    }

    public static void Write(Span<byte> raw, int bitpix, int index, double stored)
    {
        int width = Width(bitpix);
        var slice = raw.Slice(index * width, width);
        switch (bitpix)
        {
            case 8:
                slice[0] = (byte)stored;
                break;
            case 16:
                BinaryPrimitives.WriteInt16BigEndian(slice, (short)stored);
                break;
            case 32:
                BinaryPrimitives.WriteInt32BigEndian(slice, (int)stored);
                break;
            case 64:
                BinaryPrimitives.WriteInt64BigEndian(slice, (long)stored);
                break;
            case -32:
                BinaryPrimitives.WriteSingleBigEndian(slice, (float)stored);
                break;
            case -64:
                BinaryPrimitives.WriteDoubleBigEndian(slice, stored);
                break;
            default:
                throw new FitsException($"BITPIX {bitpix} is not a legal FITS value.", new FitsLocation(Keyword: "BITPIX"));
        }
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
}
