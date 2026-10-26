using System.Numerics;
using System.Runtime.CompilerServices;

namespace CFITS.Data;

/// <summary>
/// Floating FITS pixels (BITPIX -32 / -64). C# cannot share one generic constraint with integer <see cref="PixelFormat{T}"/>.
/// </summary>
internal sealed class PixelFormatReal<T> : IPixelFormat
    where T : unmanaged, IFloatingPointIeee754<T>
{
    public PixelFormatReal(int bitpix) => Bitpix = bitpix;

    public int Bitpix { get; }

    public int Width => Unsafe.SizeOf<T>();

    public bool SupportsIntegerStorage => false;

    public double Read(ReadOnlySpan<byte> raw, int index) =>
        double.CreateChecked(Pixels.ReadBigEndian<T>(raw, index * Width, Width));

    public long ReadInteger(ReadOnlySpan<byte> raw, int index) =>
        throw new FitsException("Stored integers are only defined for integer BITPIX.", new FitsLocation(Keyword: "BITPIX"));

    public void Write(Span<byte> destination, double stored) =>
        Pixels.WriteBigEndian(destination, T.CreateChecked(stored));

    public void Copy(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank, Span<double> destination) =>
        Pixels.CopyReal<T>(raw, length, scale, zero, blank, destination);

    public void Copy(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank, Span<float> destination) =>
        Pixels.CopyReal<T>(raw, length, scale, zero, blank, destination);

    public void Copy(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank, double[,] destination, int n1) =>
        Pixels.CopyReal2D<T>(raw, length, scale, zero, blank, destination, n1);

    public void Copy(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank, double[,,] destination, int n1, int n2) =>
        Pixels.CopyReal3D<T>(raw, length, scale, zero, blank, destination, n1, n2);

    public void Copy(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank, float[,] destination, int n1) =>
        Pixels.CopyReal2DFloat<T>(raw, length, scale, zero, blank, destination, n1);

    public void Copy(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank, float[,,] destination, int n1, int n2) =>
        Pixels.CopyReal3DFloat<T>(raw, length, scale, zero, blank, destination, n1, n2);

    public double Sum(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank) =>
        Pixels.SumReal<T>(raw, length, scale, zero, blank);
}
