using System.Numerics;

namespace CFITS;

internal static class Rice
{
    public static uint[] Decompress(ReadOnlySpan<byte> source, int pixels, int byteWidth, int blockSize)
    {
        int fsBits = byteWidth switch
        {
            1 => 3,
            2 => 4,
            4 => 5,
            _ => throw new FitsException("Rice compression supports 1-, 2-, and 4-byte pixels.", new FitsLocation()),
        };
        int fsMax = byteWidth switch
        {
            1 => 6,
            2 => 14,
            _ => 25,
        };
        int directBits = 1 << fsBits;
        uint mask = byteWidth == 4 ? uint.MaxValue : (1u << (byteWidth * 8)) - 1;
        if (source.Length < byteWidth)
        {
            throw new FitsException("A Rice tile is shorter than one pixel.", new FitsLocation());
        }

        uint last = 0;
        for (int index = 0; index < byteWidth; index++)
        {
            last = (last << 8) | source[index];
        }

        int position = byteWidth;
        uint buffer = Read(source, ref position);
        int remaining = 8;
        var output = new uint[pixels];
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
                    output[pixel++] = last;
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
                    output[pixel++] = last;
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
                    output[pixel++] = last;
                }
            }
        }

        return output;
    }

    private static uint Read(ReadOnlySpan<byte> source, ref int position)
    {
        if ((uint)position >= (uint)source.Length)
        {
            throw new FitsException("A Rice tile ended before all pixels were decoded.", new FitsLocation());
        }

        return source[position++];
    }

    private static uint Mask(int bits) => bits <= 0 ? 0 : (1u << bits) - 1;

    private static int HighestBit(uint value) => value == 0 ? 0 : BitOperations.Log2(value) + 1;

    private static uint Unmap(uint value) => (value & 1) == 0 ? value >> 1 : ~(value >> 1);
}
