using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace CFITS;

/// <summary>
/// Converts between FITS big-endian element bytes and host values.
/// Every bit pattern of the element width is preserved, including NaN payloads,
/// negative zero, and tails shorter than one 16-byte vector. On little-endian
/// hosts each element is byte-reversed. On big-endian hosts the bytes are copied.
/// Source and destination may be the same memory and must not partially overlap.
/// <see cref="Vector128.Shuffle(Vector128{byte}, Vector128{byte})"/> runs only when
/// SSSE3 or AdvSimd.Arm64 can execute it. Every other host uses <see cref="BinaryPrimitives"/>.
/// </summary>
public static class FitsEndian
{
    private static readonly Vector128<byte> Int16Mask = Vector128.Create(
        (byte)1, 0, 3, 2, 5, 4, 7, 6, 9, 8, 11, 10, 13, 12, 15, 14);

    private static readonly Vector128<byte> Int32Mask = Vector128.Create(
        (byte)3, 2, 1, 0, 7, 6, 5, 4, 11, 10, 9, 8, 15, 14, 13, 12);

    private static readonly Vector128<byte> Int64Mask = Vector128.Create(
        (byte)7, 6, 5, 4, 3, 2, 1, 0, 15, 14, 13, 12, 11, 10, 9, 8);

    public static void ReadInt16(ReadOnlySpan<byte> source, Span<short> destination) =>
        Read(source, destination, sizeof(short), 8, Int16Mask);

    public static void ReadInt32(ReadOnlySpan<byte> source, Span<int> destination) =>
        Read(source, destination, sizeof(int), 4, Int32Mask);

    public static void ReadInt64(ReadOnlySpan<byte> source, Span<long> destination) =>
        Read(source, destination, sizeof(long), 2, Int64Mask);

    public static void ReadSingle(ReadOnlySpan<byte> source, Span<float> destination) =>
        ReadInt32(source, MemoryMarshal.Cast<float, int>(destination));

    public static void ReadDouble(ReadOnlySpan<byte> source, Span<double> destination) =>
        ReadInt64(source, MemoryMarshal.Cast<double, long>(destination));

    public static void WriteInt16(ReadOnlySpan<short> source, Span<byte> destination) =>
        Write(MemoryMarshal.AsBytes(source), destination, sizeof(short), 8, Int16Mask);

    public static void WriteInt32(ReadOnlySpan<int> source, Span<byte> destination) =>
        Write(MemoryMarshal.AsBytes(source), destination, sizeof(int), 4, Int32Mask);

    public static void WriteInt64(ReadOnlySpan<long> source, Span<byte> destination) =>
        Write(MemoryMarshal.AsBytes(source), destination, sizeof(long), 2, Int64Mask);

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

    private static bool HasFastByteShuffle =>
        System.Runtime.Intrinsics.X86.Ssse3.IsSupported
        || System.Runtime.Intrinsics.Arm.AdvSimd.Arm64.IsSupported;

    private static void Read<T>(ReadOnlySpan<byte> source, Span<T> destination, int width, int vectorCount, Vector128<byte> mask)
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

        ref byte input = ref MemoryMarshal.GetReference(source);
        ref T output = ref MemoryMarshal.GetReference(destination);
        int index = 0;
        if (HasFastByteShuffle)
        {
            int limit = destination.Length - vectorCount;
            while (index <= limit)
            {
                Vector128<byte> value = Vector128.LoadUnsafe(ref input, (nuint)(index * width));
                Vector128.Shuffle(value, mask).StoreUnsafe(ref Unsafe.As<T, byte>(ref Unsafe.Add(ref output, index)));
                index += vectorCount;
            }
        }

        for (; index < destination.Length; index++)
        {
            ReverseOne(ref input, index * width, ref Unsafe.Add(ref output, index), width);
        }
    }

    private static void Write(ReadOnlySpan<byte> source, Span<byte> destination, int width, int vectorCount, Vector128<byte> mask)
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

        ref byte input = ref MemoryMarshal.GetReference(source);
        ref byte output = ref MemoryMarshal.GetReference(destination);
        int index = 0;
        if (HasFastByteShuffle)
        {
            int limit = count - vectorCount;
            while (index <= limit)
            {
                Vector128<byte> value = Vector128.LoadUnsafe(ref input, (nuint)(index * width));
                Vector128.Shuffle(value, mask).StoreUnsafe(ref output, (nuint)(index * width));
                index += vectorCount;
            }
        }

        for (; index < count; index++)
        {
            ReverseBytes(ref input, ref output, index * width, width);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ReverseOne<T>(ref byte input, int offset, ref T output, int width)
        where T : unmanaged
    {
        if (width == sizeof(short))
        {
            short value = BinaryPrimitives.ReverseEndianness(Unsafe.ReadUnaligned<short>(ref Unsafe.Add(ref input, offset)));
            output = Unsafe.As<short, T>(ref value);
            return;
        }

        if (width == sizeof(int))
        {
            int value = BinaryPrimitives.ReverseEndianness(Unsafe.ReadUnaligned<int>(ref Unsafe.Add(ref input, offset)));
            output = Unsafe.As<int, T>(ref value);
            return;
        }

        long wide = BinaryPrimitives.ReverseEndianness(Unsafe.ReadUnaligned<long>(ref Unsafe.Add(ref input, offset)));
        output = Unsafe.As<long, T>(ref wide);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ReverseBytes(ref byte input, ref byte output, int offset, int width)
    {
        if (width == sizeof(short))
        {
            short value = BinaryPrimitives.ReverseEndianness(Unsafe.ReadUnaligned<short>(ref Unsafe.Add(ref input, offset)));
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref output, offset), value);
            return;
        }

        if (width == sizeof(int))
        {
            int value = BinaryPrimitives.ReverseEndianness(Unsafe.ReadUnaligned<int>(ref Unsafe.Add(ref input, offset)));
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref output, offset), value);
            return;
        }

        long wide = BinaryPrimitives.ReverseEndianness(Unsafe.ReadUnaligned<long>(ref Unsafe.Add(ref input, offset)));
        Unsafe.WriteUnaligned(ref Unsafe.Add(ref output, offset), wide);
    }

    private static void Ensure(int available, int count, int width)
    {
        if (count < 0 || available < (long)count * width)
        {
            throw new FitsException("The endian buffer is shorter than the element count.", new FitsLocation());
        }
    }
}
