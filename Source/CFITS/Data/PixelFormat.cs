using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CFITS.Data;

/// <summary>
/// Integer FITS pixels (positive BITPIX). Each closed <typeparamref name="T"/> is one JIT-specialized codec.
/// </summary>
internal sealed class PixelFormat<T> : IPixelFormat
    where T : unmanaged, IBinaryInteger<T>
{
    public PixelFormat(int bitpix) => Bitpix = bitpix;

    public int Bitpix { get; }

    public int Width => Unsafe.SizeOf<T>();

    public bool SupportsIntegerStorage => true;

    public double Read(ReadOnlySpan<byte> raw, int index) =>
        double.CreateChecked(Pixels.ReadBigEndian<T>(raw, index * Width, Width));

    public long ReadInteger(ReadOnlySpan<byte> raw, int index) =>
        long.CreateChecked(Pixels.ReadBigEndian<T>(raw, index * Width, Width));

    public void Write(Span<byte> destination, double stored) =>
        Pixels.WriteBigEndian(destination, T.CreateChecked(stored));

    public void Copy(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank, Span<double> destination) =>
        Pixels.CopyInteger<T>(raw, length, scale, zero, blank, destination);

    public void Copy(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank, Span<float> destination) =>
        Pixels.CopyInteger<T>(raw, length, scale, zero, blank, destination);

    public void Copy(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank, double[,] destination, int n1) =>
        Pixels.CopyInteger2D<T>(raw, length, scale, zero, blank, destination, n1);

    public void Copy(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank, double[,,] destination, int n1, int n2) =>
        Pixels.CopyInteger3D<T>(raw, length, scale, zero, blank, destination, n1, n2);

    public void Copy(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank, float[,] destination, int n1) =>
        Pixels.CopyInteger2DFloat<T>(raw, length, scale, zero, blank, destination, n1);

    public void Copy(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank, float[,,] destination, int n1, int n2) =>
        Pixels.CopyInteger3DFloat<T>(raw, length, scale, zero, blank, destination, n1, n2);

    public double Sum(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank) =>
        Pixels.SumInteger<T>(raw, length, scale, zero, blank);
}
