using System.Runtime.CompilerServices;

namespace CFITS.Data;

internal static partial class Pixels
{
    internal static void DispatchWrite(int bitpix, Span<byte> destination, double stored)
    {
        switch (bitpix)
        {
            case 8:
                WriteBigEndian(destination, (byte)stored);
                return;
            case 16:
                WriteBigEndian(destination, (short)stored);
                return;
            case 32:
                WriteBigEndian(destination, (int)stored);
                return;
            case 64:
                WriteBigEndian(destination, (long)stored);
                return;
            case -32:
                WriteBigEndian(destination, (float)stored);
                return;
            case -64:
                WriteBigEndian(destination, stored);
                return;
            default:
                ThrowIllegalBitpix(bitpix);
                return;
        }
    }

    internal static void DispatchCopy(
        ReadOnlySpan<byte> raw,
        int bitpix,
        int length,
        double scale,
        double zero,
        long? blank,
        Span<double> destination)
    {
        switch (bitpix)
        {
            case 8:
                CopyInteger<byte>(raw, length, scale, zero, blank, destination);
                return;
            case 16:
                CopyInteger<short>(raw, length, scale, zero, blank, destination);
                return;
            case 32:
                CopyInteger<int>(raw, length, scale, zero, blank, destination);
                return;
            case 64:
                CopyInteger<long>(raw, length, scale, zero, blank, destination);
                return;
            case -32:
                CopyReal<float>(raw, length, scale, zero, blank, destination);
                return;
            case -64:
                CopyReal<double>(raw, length, scale, zero, blank, destination);
                return;
            default:
                ThrowIllegalBitpix(bitpix);
                return;
        }
    }

    internal static void DispatchCopy(
        ReadOnlySpan<byte> raw,
        int bitpix,
        int length,
        double scale,
        double zero,
        long? blank,
        Span<float> destination)
    {
        switch (bitpix)
        {
            case 8:
                CopyInteger<byte>(raw, length, scale, zero, blank, destination);
                return;
            case 16:
                CopyInteger<short>(raw, length, scale, zero, blank, destination);
                return;
            case 32:
                CopyInteger<int>(raw, length, scale, zero, blank, destination);
                return;
            case 64:
                CopyInteger<long>(raw, length, scale, zero, blank, destination);
                return;
            case -32:
                CopyReal<float>(raw, length, scale, zero, blank, destination);
                return;
            case -64:
                CopyReal<double>(raw, length, scale, zero, blank, destination);
                return;
            default:
                ThrowIllegalBitpix(bitpix);
                return;
        }
    }

    internal static void DispatchCopy(
        ReadOnlySpan<byte> raw,
        int bitpix,
        int length,
        double scale,
        double zero,
        long? blank,
        double[,] destination,
        int n1)
    {
        switch (bitpix)
        {
            case 8:
                CopyInteger2D<byte>(raw, length, scale, zero, blank, destination, n1);
                return;
            case 16:
                CopyInteger2D<short>(raw, length, scale, zero, blank, destination, n1);
                return;
            case 32:
                CopyInteger2D<int>(raw, length, scale, zero, blank, destination, n1);
                return;
            case 64:
                CopyInteger2D<long>(raw, length, scale, zero, blank, destination, n1);
                return;
            case -32:
                CopyReal2D<float>(raw, length, scale, zero, blank, destination, n1);
                return;
            case -64:
                CopyReal2D<double>(raw, length, scale, zero, blank, destination, n1);
                return;
            default:
                ThrowIllegalBitpix(bitpix);
                return;
        }
    }

    internal static void DispatchCopy(
        ReadOnlySpan<byte> raw,
        int bitpix,
        int length,
        double scale,
        double zero,
        long? blank,
        double[,,] destination,
        int n1,
        int n2)
    {
        switch (bitpix)
        {
            case 8:
                CopyInteger3D<byte>(raw, length, scale, zero, blank, destination, n1, n2);
                return;
            case 16:
                CopyInteger3D<short>(raw, length, scale, zero, blank, destination, n1, n2);
                return;
            case 32:
                CopyInteger3D<int>(raw, length, scale, zero, blank, destination, n1, n2);
                return;
            case 64:
                CopyInteger3D<long>(raw, length, scale, zero, blank, destination, n1, n2);
                return;
            case -32:
                CopyReal3D<float>(raw, length, scale, zero, blank, destination, n1, n2);
                return;
            case -64:
                CopyReal3D<double>(raw, length, scale, zero, blank, destination, n1, n2);
                return;
            default:
                ThrowIllegalBitpix(bitpix);
                return;
        }
    }

    internal static void DispatchCopy(
        ReadOnlySpan<byte> raw,
        int bitpix,
        int length,
        double scale,
        double zero,
        long? blank,
        float[,] destination,
        int n1)
    {
        switch (bitpix)
        {
            case 8:
                CopyInteger2DFloat<byte>(raw, length, scale, zero, blank, destination, n1);
                return;
            case 16:
                CopyInteger2DFloat<short>(raw, length, scale, zero, blank, destination, n1);
                return;
            case 32:
                CopyInteger2DFloat<int>(raw, length, scale, zero, blank, destination, n1);
                return;
            case 64:
                CopyInteger2DFloat<long>(raw, length, scale, zero, blank, destination, n1);
                return;
            case -32:
                CopyReal2DFloat<float>(raw, length, scale, zero, blank, destination, n1);
                return;
            case -64:
                CopyReal2DFloat<double>(raw, length, scale, zero, blank, destination, n1);
                return;
            default:
                ThrowIllegalBitpix(bitpix);
                return;
        }
    }

    internal static void DispatchCopy(
        ReadOnlySpan<byte> raw,
        int bitpix,
        int length,
        double scale,
        double zero,
        long? blank,
        float[,,] destination,
        int n1,
        int n2)
    {
        switch (bitpix)
        {
            case 8:
                CopyInteger3DFloat<byte>(raw, length, scale, zero, blank, destination, n1, n2);
                return;
            case 16:
                CopyInteger3DFloat<short>(raw, length, scale, zero, blank, destination, n1, n2);
                return;
            case 32:
                CopyInteger3DFloat<int>(raw, length, scale, zero, blank, destination, n1, n2);
                return;
            case 64:
                CopyInteger3DFloat<long>(raw, length, scale, zero, blank, destination, n1, n2);
                return;
            case -32:
                CopyReal3DFloat<float>(raw, length, scale, zero, blank, destination, n1, n2);
                return;
            case -64:
                CopyReal3DFloat<double>(raw, length, scale, zero, blank, destination, n1, n2);
                return;
            default:
                ThrowIllegalBitpix(bitpix);
                return;
        }
    }

    internal static double DispatchSum(ReadOnlySpan<byte> raw, int bitpix, int length, double scale, double zero, long? blank)
    {
        switch (bitpix)
        {
            case 8:
                return SumInteger<byte>(raw, length, scale, zero, blank);
            case 16:
                return SumInteger<short>(raw, length, scale, zero, blank);
            case 32:
                return SumInteger<int>(raw, length, scale, zero, blank);
            case 64:
                return SumInteger<long>(raw, length, scale, zero, blank);
            case -32:
                return SumReal<float>(raw, length, scale, zero, blank);
            case -64:
                return SumReal<double>(raw, length, scale, zero, blank);
            default:
                ThrowIllegalBitpix(bitpix);
                return 0;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowIllegalBitpix(int bitpix) =>
        throw new FitsException($"BITPIX {bitpix} is not a legal FITS value.", new FitsLocation(Keyword: "BITPIX"));
}
