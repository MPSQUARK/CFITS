using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CFITS;

public static partial class FitsEndian
{
    private static void SwapShorts(ReadOnlySpan<byte> source, Span<short> destination)
    {
        ref byte input = ref MemoryMarshal.GetReference(source);
        ref short output = ref MemoryMarshal.GetReference(destination);
        int index = 0;
        nuint offset = 0;
        int lanes = Vector<ushort>.Count;
        int byteStride = lanes * sizeof(ushort);
        int limit = destination.Length - lanes;
        while (index <= limit)
        {
            Vector<ushort> value = Vector.LoadUnsafe(ref Unsafe.As<byte, ushort>(ref Unsafe.Add(ref input, offset)));
            Vector<ushort> swapped = (value << 8) | (value >> 8);
            swapped.StoreUnsafe(ref Unsafe.As<short, ushort>(ref Unsafe.Add(ref output, index)));
            index += lanes;
            offset += (nuint)byteStride;
        }

        for (; index < destination.Length; index++)
        {
            Unsafe.Add(ref output, index) = BinaryPrimitives.ReverseEndianness(
                Unsafe.ReadUnaligned<short>(ref Unsafe.Add(ref input, offset)));
            offset += sizeof(short);
        }
    }

    private static void SwapInts(ReadOnlySpan<byte> source, Span<int> destination)
    {
        ref byte input = ref MemoryMarshal.GetReference(source);
        ref int output = ref MemoryMarshal.GetReference(destination);
        int index = 0;
        nuint offset = 0;
        int lanes = Vector<uint>.Count;
        int byteStride = lanes * sizeof(uint);
        int limit = destination.Length - lanes;
        Vector<uint> lowMask = new(0x00FF00FFu);
        Vector<uint> highMask = new(0xFF00FF00u);
        while (index <= limit)
        {
            Vector<uint> value = Vector.LoadUnsafe(ref Unsafe.As<byte, uint>(ref Unsafe.Add(ref input, offset)));
            Vector<uint> swapped = ((value & lowMask) << 8) | ((value & highMask) >> 8);
            swapped = (swapped << 16) | (swapped >> 16);
            swapped.StoreUnsafe(ref Unsafe.As<int, uint>(ref Unsafe.Add(ref output, index)));
            index += lanes;
            offset += (nuint)byteStride;
        }

        for (; index < destination.Length; index++)
        {
            Unsafe.Add(ref output, index) = BinaryPrimitives.ReverseEndianness(
                Unsafe.ReadUnaligned<int>(ref Unsafe.Add(ref input, offset)));
            offset += sizeof(int);
        }
    }

    private static void SwapLongs(ReadOnlySpan<byte> source, Span<long> destination)
    {
        ref byte input = ref MemoryMarshal.GetReference(source);
        ref long output = ref MemoryMarshal.GetReference(destination);
        int index = 0;
        nuint offset = 0;
        int lanes = Vector<ulong>.Count;
        int byteStride = lanes * sizeof(ulong);
        int limit = destination.Length - lanes;
        Vector<ulong> mask8 = new(0x00FF00FF00FF00FFul);
        Vector<ulong> mask8High = new(0xFF00FF00FF00FF00ul);
        Vector<ulong> mask16 = new(0x0000FFFF0000FFFFul);
        Vector<ulong> mask16High = new(0xFFFF0000FFFF0000ul);
        Vector<ulong> mask32 = new(0x00000000FFFFFFFFul);
        Vector<ulong> mask32High = new(0xFFFFFFFF00000000ul);
        while (index <= limit)
        {
            Vector<ulong> value = Vector.LoadUnsafe(ref Unsafe.As<byte, ulong>(ref Unsafe.Add(ref input, offset)));
            Vector<ulong> swapped = ((value & mask32) << 32) | ((value & mask32High) >> 32);
            swapped = ((swapped & mask16) << 16) | ((swapped & mask16High) >> 16);
            swapped = ((swapped & mask8) << 8) | ((swapped & mask8High) >> 8);
            swapped.StoreUnsafe(ref Unsafe.As<long, ulong>(ref Unsafe.Add(ref output, index)));
            index += lanes;
            offset += (nuint)byteStride;
        }

        for (; index < destination.Length; index++)
        {
            Unsafe.Add(ref output, index) = BinaryPrimitives.ReverseEndianness(
                Unsafe.ReadUnaligned<long>(ref Unsafe.Add(ref input, offset)));
            offset += sizeof(long);
        }
    }
}
