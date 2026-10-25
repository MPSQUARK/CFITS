using System.Buffers.Binary;

namespace CFITS.Tests;

public sealed class BitpixByteVectorComplianceTests
{
    [Fact]
    public void WritesAndReadsIntegerBoundaryByteVectors()
    {
        AssertImage([0x00, 0xFF], 8, new byte[] { 0, 255 });
        AssertImage([0x80, 0x00, 0x7F, 0xFF], 16, new short[] { short.MinValue, short.MaxValue });
        AssertImage(
            [0x80, 0x00, 0x00, 0x00, 0x7F, 0xFF, 0xFF, 0xFF],
            32,
            new[] { int.MinValue, int.MaxValue });
        AssertImage(
            [0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
             0x7F, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF],
            64,
            new[] { long.MinValue, long.MaxValue });
    }

    [Fact]
    public void PreservesIEEE754SpecialValueByteVectors()
    {
        float[] single = [float.PositiveInfinity, float.NegativeInfinity, float.NaN];
        using var singleDocument = FitsDocumentBuilder.Create().Image([3], single).Build();
        Assert.Equal(
            [0x7F, 0x80, 0x00, 0x00, 0xFF, 0x80, 0x00, 0x00, 0xFF, 0xC0, 0x00, 0x00],
            singleDocument.ToArray().AsSpan(2880, 12).ToArray());

        double[] doubleValues = [double.PositiveInfinity, double.NegativeInfinity, double.NaN];
        using var doubleDocument = FitsDocumentBuilder.Create().Image([3], doubleValues).Build();
        byte[] bytes = doubleDocument.ToArray();
        Assert.Equal(0x7FF0000000000000UL, BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(2880)));
        Assert.Equal(0xFFF0000000000000UL, BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(2888)));
        Assert.True(double.IsNaN(doubleDocument.Hdus[0].Image!.ReadPhysical(2)));
    }

    private static void AssertImage<T>(byte[] expected, int bitpix, T[] values)
        where T : unmanaged
    {
        using var document = bitpix switch
        {
            8 => FitsDocumentBuilder.Create().Image([values.Length], (byte[])(object)values).Build(),
            16 => FitsDocumentBuilder.Create().Image([values.Length], (short[])(object)values).Build(),
            32 => FitsDocumentBuilder.Create().Image([values.Length], (int[])(object)values).Build(),
            64 => FitsDocumentBuilder.Create().Image([values.Length], (long[])(object)values).Build(),
            _ => throw new ArgumentOutOfRangeException(nameof(bitpix)),
        };

        Assert.Equal(expected, document.ToArray().AsSpan(2880, expected.Length).ToArray());
        Assert.Equal(values.Length, document.Hdus[0].Image!.Length);
    }
}
