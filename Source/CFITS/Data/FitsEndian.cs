using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CFITS;

/// <summary>
/// Converts between FITS big-endian element bytes and host values.
/// Every bit pattern of the element width is preserved, including NaN payloads,
/// negative zero, and tails shorter than one hardware vector.
/// Source and destination may be the same memory and must not partially overlap.
/// Little-endian hosts use <see cref="Vector{T}"/> byte swaps, which the JIT
/// widens to the processor's SIMD width. Big-endian hosts copy the bytes.
/// </summary>
public static partial class FitsEndian
{
    public static void ReadInt16(ReadOnlySpan<byte> source, Span<short> destination) =>
        Read(source, destination, sizeof(short));

    public static void ReadInt32(ReadOnlySpan<byte> source, Span<int> destination) =>
        Read(source, destination, sizeof(int));

    public static void ReadInt64(ReadOnlySpan<byte> source, Span<long> destination) =>
        Read(source, destination, sizeof(long));

    public static void ReadSingle(ReadOnlySpan<byte> source, Span<float> destination) =>
        ReadInt32(source, MemoryMarshal.Cast<float, int>(destination));

    public static void ReadDouble(ReadOnlySpan<byte> source, Span<double> destination) =>
        ReadInt64(source, MemoryMarshal.Cast<double, long>(destination));

    public static void WriteInt16(ReadOnlySpan<short> source, Span<byte> destination) =>
        Write(MemoryMarshal.AsBytes(source), destination, sizeof(short));

    public static void WriteInt32(ReadOnlySpan<int> source, Span<byte> destination) =>
        Write(MemoryMarshal.AsBytes(source), destination, sizeof(int));

    public static void WriteInt64(ReadOnlySpan<long> source, Span<byte> destination) =>
        Write(MemoryMarshal.AsBytes(source), destination, sizeof(long));

    public static void WriteSingle(ReadOnlySpan<float> source, Span<byte> destination) =>
        WriteInt32(MemoryMarshal.Cast<float, int>(source), destination);

    public static void WriteDouble(ReadOnlySpan<double> source, Span<byte> destination) =>
        WriteInt64(MemoryMarshal.Cast<double, long>(source), destination);

    public static short ReadInt16(ReadOnlySpan<byte> source, int offset) =>
        BinaryPrimitives.ReadInt16BigEndian(source.Slice(offset, sizeof(short)));

    public static int ReadInt32(ReadOnlySpan<byte> source, int offset) =>
        BinaryPrimitives.ReadInt32BigEndian(source.Slice(offset, sizeof(int)));

    public static long ReadInt64(ReadOnlySpan<byte> source, int offset) =>
        BinaryPrimitives.ReadInt64BigEndian(source.Slice(offset, sizeof(long)));

    public static float ReadSingle(ReadOnlySpan<byte> source, int offset) =>
        BinaryPrimitives.ReadSingleBigEndian(source.Slice(offset, sizeof(float)));

    public static double ReadDouble(ReadOnlySpan<byte> source, int offset) =>
        BinaryPrimitives.ReadDoubleBigEndian(source.Slice(offset, sizeof(double)));

    public static Span<T> AsContiguous<T>(T[,] values)
        where T : unmanaged =>
        MemoryMarshal.CreateSpan(ref Unsafe.As<byte, T>(ref MemoryMarshal.GetArrayDataReference(values)), values.Length);

    public static Span<T> AsContiguous<T>(T[,,] values)
        where T : unmanaged =>
        MemoryMarshal.CreateSpan(ref Unsafe.As<byte, T>(ref MemoryMarshal.GetArrayDataReference(values)), values.Length);

    private static void Read<T>(ReadOnlySpan<byte> source, Span<T> destination, int width)
        where T : unmanaged
    {
        Ensure(source.Length, destination.Length, width);
        if (destination.IsEmpty)
        {
            return;
        }

        if (!BitConverter.IsLittleEndian)
        {
            MemoryMarshal.Cast<byte, T>(source[..(destination.Length * width)]).CopyTo(destination);
            return;
        }

        if (width == sizeof(short))
        {
            SwapShorts(source, MemoryMarshal.Cast<T, short>(destination));
            return;
        }

        if (width == sizeof(int))
        {
            SwapInts(source, MemoryMarshal.Cast<T, int>(destination));
            return;
        }

        SwapLongs(source, MemoryMarshal.Cast<T, long>(destination));
    }

    private static void Write(ReadOnlySpan<byte> source, Span<byte> destination, int width)
    {
        int count = source.Length / width;
        Ensure(destination.Length, count, width);
        if (count == 0)
        {
            return;
        }

        if (!BitConverter.IsLittleEndian)
        {
            source[..(count * width)].CopyTo(destination);
            return;
        }

        if (width == sizeof(short))
        {
            SwapShorts(source, MemoryMarshal.Cast<byte, short>(destination)[..count]);
            return;
        }

        if (width == sizeof(int))
        {
            SwapInts(source, MemoryMarshal.Cast<byte, int>(destination)[..count]);
            return;
        }

        SwapLongs(source, MemoryMarshal.Cast<byte, long>(destination)[..count]);
    }

    private static void Ensure(int available, int count, int width)
    {
        if (count < 0 || available < (long)count * width)
        {
            throw new FitsException("The endian buffer is shorter than the element count.", new FitsLocation());
        }
    }
}
