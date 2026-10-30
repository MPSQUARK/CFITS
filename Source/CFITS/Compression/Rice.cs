using System.Buffers.Binary;
using System.Numerics;

namespace CFITS.Compression;

internal static class Rice
{
    public static uint[] Decompress(ReadOnlySpan<byte> source, int pixels, int byteWidth, int blockSize)
    {
        ValidateArguments(pixels, byteWidth, blockSize, source);
        var output = pixels == 0 ? [] : new uint[pixels];
        Decode(source, pixels, byteWidth, blockSize, new UIntSink(output));
        return output;
    }

    /// <summary>
    /// Writes the same big-endian pixel bytes <see cref="Decompress"/> would pack,
    /// directly into <paramref name="destination"/>.
    /// </summary>
    internal static void WriteBigEndian(ReadOnlySpan<byte> source, Span<byte> destination, int pixels, int byteWidth, int blockSize)
    {
        ValidateArguments(pixels, byteWidth, blockSize, source);
        if (pixels > 0 && destination.Length < checked(pixels * byteWidth))
        {
            throw new FitsException("The Rice destination is shorter than the tile.", new FitsLocation());
        }

        Decode(source, pixels, byteWidth, blockSize, new ByteSink(destination, byteWidth));
    }

    private static void Decode<TSink>(ReadOnlySpan<byte> source, int pixels, int byteWidth, int blockSize, TSink sink)
        where TSink : IRiceSink, allows ref struct
    {
        ValidateArguments(pixels, byteWidth, blockSize, source);
        int fsBits = byteWidth switch
        {
            1 => 3,
            2 => 4,
            _ => 5,
        };
        int fsMax = byteWidth switch
        {
            1 => 6,
            2 => 14,
            _ => 25,
        };
        int directBits = 1 << fsBits;
        uint mask = byteWidth == 4 ? uint.MaxValue : (1u << (byteWidth * 8)) - 1;
        uint last = 0;
        for (int index = 0; index < byteWidth; index++)
        {
            last = (last << 8) | source[index];
        }

        int position = byteWidth;
        uint buffer = Read(source, ref position);
        int remaining = 8;
        int pixel = 0;
        while (pixel < pixels)
        {
            remaining -= fsBits;
            while (remaining < 0)
            {
                buffer = (buffer << 8) | Read(source, ref position);
                remaining += 8;
            }

            int fs = (int)(buffer >> remaining) - 1;
            buffer &= Mask(remaining);
            int limit = Math.Min(pixel + blockSize, pixels);
            if (fs < 0)
            {
                while (pixel < limit)
                {
                    sink.Store(pixel++, last);
                }
            }
            else if (fs == fsMax)
            {
                while (pixel < limit)
                {
                    int shift = directBits - remaining;
                    ulong value = (ulong)buffer << shift;
                    for (shift -= 8; shift >= 0; shift -= 8)
                    {
                        buffer = Read(source, ref position);
                        value |= (ulong)buffer << shift;
                    }

                    if (remaining > 0)
                    {
                        buffer = Read(source, ref position);
                        value |= (ulong)buffer >> -shift;
                        buffer &= Mask(remaining);
                    }
                    else
                    {
                        buffer = 0;
                    }

                    uint difference = Unmap((uint)value);
                    last = (last + difference) & mask;
                    sink.Store(pixel++, last);
                }
            }
            else
            {
                while (pixel < limit)
                {
                    while (buffer == 0)
                    {
                        remaining += 8;
                        buffer = Read(source, ref position);
                    }

                    int zeros = remaining - HighestBit(buffer);
                    remaining -= zeros + 1;
                    buffer ^= 1u << remaining;
                    remaining -= fs;
                    while (remaining < 0)
                    {
                        buffer = (buffer << 8) | Read(source, ref position);
                        remaining += 8;
                    }

                    uint difference = Unmap(((uint)zeros << fs) | (buffer >> remaining));
                    buffer &= Mask(remaining);
                    last = (last + difference) & mask;
                    sink.Store(pixel++, last);
                }
            }
        }

    }

    private static void ValidateArguments(int pixels, int byteWidth, int blockSize, ReadOnlySpan<byte> source)
    {
        if (pixels < 0 || blockSize <= 0)
        {
            throw new FitsException("Rice pixel and block counts must be non-negative and positive.", new FitsLocation());
        }

        if (byteWidth is not (1 or 2 or 4))
        {
            throw new FitsException("Rice compression supports 1-, 2-, and 4-byte pixels.", new FitsLocation());
        }

        if (source.Length < byteWidth)
        {
            throw new FitsException("A Rice tile is shorter than one pixel.", new FitsLocation());
        }
    }

    private interface IRiceSink
    {
        void Store(int pixel, uint value);
    }

    private readonly ref struct UIntSink : IRiceSink
    {
        private readonly Span<uint> _output;

        public UIntSink(Span<uint> output) => _output = output;

        public void Store(int pixel, uint value) => _output[pixel] = value;
    }

    private readonly ref struct ByteSink : IRiceSink
    {
        private readonly Span<byte> _output;
        private readonly int _width;

        public ByteSink(Span<byte> output, int width)
        {
            _output = output;
            _width = width;
        }

        public void Store(int pixel, uint value)
        {
            int offset = pixel * _width;
            if (_width == 1)
            {
                _output[offset] = (byte)value;
                return;
            }

            if (_width == 2)
            {
                BinaryPrimitives.WriteUInt16BigEndian(_output.Slice(offset, 2), (ushort)value);
                return;
            }

            BinaryPrimitives.WriteUInt32BigEndian(_output.Slice(offset, 4), value);
        }
    }

    private static uint Read(ReadOnlySpan<byte> source, ref int position)
    {
        if ((uint)position >= (uint)source.Length)
        {
            throw new FitsException("A Rice tile ended before all pixels were decoded.", new FitsLocation());
        }

        return source[position++];
    }

    private static uint Mask(int bits) => bits switch
    {
        <= 0 => 0,
        >= 32 => uint.MaxValue,
        _ => (1u << bits) - 1,
    };

    private static int HighestBit(uint value) => value == 0 ? 0 : BitOperations.Log2(value) + 1;

    private static uint Unmap(uint value) => (value & 1) == 0 ? value >> 1 : ~(value >> 1);
}
