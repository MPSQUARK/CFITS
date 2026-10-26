using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace CFITS.Tests;

public sealed class FitsEndianTests
{
    [Fact]
    public void PreservesEveryInt16BitPatternIncludingPartialVectors()
    {
        short[] values = new short[65536];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = (short)index;
        }

        byte[] encoded = new byte[values.Length * sizeof(short)];
        FitsEndian.WriteInt16(values, encoded);
        Span<byte> expected = stackalloc byte[sizeof(short)];
        for (int index = 0; index < values.Length; index++)
        {
            BinaryPrimitives.WriteInt16BigEndian(expected, values[index]);
            Assert.True(expected.SequenceEqual(encoded.AsSpan(index * sizeof(short), sizeof(short))));
        }

        short[] decoded = new short[values.Length];
        FitsEndian.ReadInt16(encoded, decoded);
        Assert.Equal(values, decoded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    public void PreservesInt32Boundaries(int length)
    {
        int[] values = new int[length];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = index switch
            {
                0 => int.MinValue,
                1 => -1,
                2 => 0,
                3 => int.MaxValue,
                4 => unchecked((int)0x7FC00001),
                _ => index * 17,
            };
        }

        byte[] encoded = new byte[values.Length * sizeof(int)];
        FitsEndian.WriteInt32(values, encoded);
        int[] decoded = new int[values.Length];
        FitsEndian.ReadInt32(encoded, decoded);
        Assert.Equal(values, decoded);
        for (int index = 0; index < values.Length; index++)
        {
            Assert.Equal(
                BinaryPrimitives.ReadInt32BigEndian(encoded.AsSpan(index * sizeof(int), sizeof(int))),
                decoded[index]);
        }
    }

    [Fact]
    public void PreservesFloatAndDoublePayloads()
    {
        float[] floats =
        [
            0f,
            -0f,
            float.Epsilon,
            float.MaxValue,
            float.NegativeInfinity,
            BitConverter.Int32BitsToSingle(unchecked((int)0x7F800001)),
        ];
        double[] doubles =
        [
            0d,
            -0d,
            double.Epsilon,
            double.MaxValue,
            double.NaN,
            BitConverter.Int64BitsToDouble(0x7FF0000000000001),
        ];
        AssertBits(floats, FitsEndian.WriteSingle, FitsEndian.ReadSingle);
        AssertBits(doubles, FitsEndian.WriteDouble, FitsEndian.ReadDouble);
    }

    [Fact]
    public void DirectImageReadMatchesTheDocumentPixels()
    {
        short[,] source = new short[3, 5];
        for (int y = 0; y < source.GetLength(0); y++)
        {
            for (int x = 0; x < source.GetLength(1); x++)
            {
                source[y, x] = (short)((y * 5) + x - 3);
            }
        }

        using var stream = new MemoryStream();
        FitsDocumentBuilder.Create()
            .Image([5, 3], FitsEndian.AsContiguous(source))
            .Write(stream);
        stream.Position = 0;
        Assert.True(FitsDirectImage.TryRead<short>(stream, 2, -1, null, new FitsOpenOptions(), out Array? pixels, out FitsHeader? header));
        var read = Assert.IsType<short[,]>(pixels);
        Assert.NotNull(header);
        Assert.Equal(source, read);

        stream.Position = 0;
        using var document = FitsDocument.Open(stream);
        var copied = new short[3, 5];
        document.Hdus[0].Image!.CopyInteger(copied);
        Assert.Equal(source, copied);
    }

    private static void AssertBits<T>(T[] values, Action<ReadOnlySpan<T>, Span<byte>> write, Action<ReadOnlySpan<byte>, Span<T>> read)
        where T : unmanaged
    {
        byte[] encoded = new byte[values.Length * System.Runtime.CompilerServices.Unsafe.SizeOf<T>()];
        write(values, encoded);
        T[] decoded = new T[values.Length];
        read(encoded, decoded);
        Assert.True(MemoryMarshal.AsBytes(values.AsSpan()).SequenceEqual(MemoryMarshal.AsBytes(decoded.AsSpan())));
    }
}
