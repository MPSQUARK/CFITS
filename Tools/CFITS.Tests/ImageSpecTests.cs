namespace CFITS.Tests;

public sealed class ImageSpecTests
{
    [Fact]
    public void SupportsEveryImplementedBitpixAndWritesBigEndianData()
    {
        AssertImageBytes([0x12, 0xFE], 8, new byte[] { 0x12, 0xFE });
        AssertImageBytes([0xFF, 0xFE, 0x12, 0x34], 16, new short[] { -2, 0x1234 });
        AssertImageBytes([0xFF, 0xFF, 0xFF, 0xFE, 0x12, 0x34, 0x56, 0x78], 32, new[] { -2, 0x12345678 });
        AssertImageBytes(
            [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFE, 0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE, 0xF0],
            64,
            new long[] { -2, 0x123456789ABCDEF0 });
        AssertImageBytes([0x3F, 0xC0, 0x00, 0x00, 0xC0, 0x20, 0x00, 0x00], -32, new[] { 1.5f, -2.5f });
        AssertImageBytes(
            [0x3F, 0xF8, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xC0, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00],
            -64,
            new[] { 1.5, -2.25 });
    }

    [Fact]
    public void UsesFirstAxisAsTheFastestVaryingAxis()
    {
        using var document = FitsDocumentBuilder.Create()
            .Image([2, 3, 2], new short[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 })
            .Build();
        FitsImage image = document.Hdus[0].Image!;

        Assert.Equal(new[] { 2, 3, 2 }, image.Axes);
        Assert.Equal(12, image.Length);
        var values = new double[2, 3, 2];
        image.CopyPhysical(values);

        for (int z = 0; z < 2; z++)
        {
            for (int y = 0; y < 3; y++)
            {
                for (int x = 0; x < 2; x++)
                {
                    int flatIndex = x + (2 * (y + (3 * z)));
                    Assert.Equal(flatIndex + 1, values[z, y, x]);
                }
            }
        }
    }

    [Fact]
    public void RepresentsZeroDimensionalAndZeroLengthImages()
    {
        using var scalar = FitsDocumentBuilder.Create()
            .Image(Array.Empty<int>(), Array.Empty<byte>())
            .Build();
        using var zeroLength = FitsDocumentBuilder.Create()
            .Image([0, 4], Array.Empty<byte>())
            .Build();

        Assert.Empty(scalar.Hdus[0].Image!.Axes);
        Assert.Equal(0, scalar.Hdus[0].Image!.Length);
        Assert.Equal(new[] { 0, 4 }, zeroLength.Hdus[0].Image!.Axes);
        Assert.Equal(0, zeroLength.Hdus[0].Image!.Length);
        Assert.Empty(scalar.Hdus[0].Image!.ReadPhysical());
        Assert.Empty(zeroLength.Hdus[0].Image!.ReadPhysical());
    }

    [Fact]
    public void AppliesBscaleAndBzeroIndependentlyToPhysicalValues()
    {
        using var document = FitsDocumentBuilder.Create()
            .Image([3], new short[] { -2, 0, 3 }, header => header
                .Real("BSCALE", 2.5)
                .Real("BZERO", 10))
            .Build();
        FitsImage image = document.Hdus[0].Image!;

        Assert.Equal(new[] { 5d, 10d, 17.5d }, image.ReadPhysical());
        Assert.Equal(5d, image.ReadPhysical(0));
        Assert.Equal(10d, image.ReadPhysical(1));
        Assert.Equal(17.5d, image.ReadPhysical(2));
    }

    [Fact]
    public void AppliesBlankOnlyToIntegerPhysicalValues()
    {
        using var integer = FitsDocumentBuilder.Create()
            .Image([3], new short[] { -2, -1, 3 }, header => header
                .Real("BSCALE", 2)
                .Real("BZERO", 10)
                .Integer("BLANK", -1))
            .Build();
        using var real = FitsDocumentBuilder.Create()
            .Image([2], new[] { 1.5f, -2.5f }, header => header.Integer("BLANK", 0))
            .Build();

        FitsImage integerImage = integer.Hdus[0].Image!;
        double[] values = integerImage.ReadPhysical();
        Assert.Equal(6d, values[0]);
        Assert.True(double.IsNaN(values[1]));
        Assert.Equal(16d, values[2]);
        Assert.Throws<FitsException>(() => integerImage.ReadInteger(1));
        Assert.Equal(6, integerImage.ReadInteger(0));

        FitsImage realImage = real.Hdus[0].Image!;
        Assert.Equal(1.5, realImage.ReadPhysical(0));
        Assert.Throws<FitsException>(() => realImage.ReadInteger(0));
    }

    [Fact]
    public void ReadsStoredIntegersExactlyAndSupportsStoredScaleMode()
    {
        long awkward = (1L << 60) + 3;
        using var document = FitsDocumentBuilder.Create()
            .Image([3], new long[] { long.MinValue, awkward, long.MaxValue },
                header => header.Real("BZERO", 9223372036854775808d))
            .Build();
        using var stored = FitsDocument.Parse(document.ToArray(), new FitsOpenOptions { ScaleMode = FitsScaleMode.Stored });

        FitsImage image = stored.Hdus[0].Image!;
        Assert.Equal((Int128)long.MinValue, image.ReadInteger(0));
        Assert.Equal((Int128)awkward, image.ReadInteger(1));
        Assert.Equal((Int128)long.MaxValue, image.ReadInteger(2));
        Assert.Equal((double)awkward, image.ReadPhysical(1));
    }

    [Fact]
    public void RejectsInvalidImageSizesDestinationsShapesAndIndexes()
    {
        Assert.Throws<FitsException>(() => FitsDocumentBuilder.Create()
            .Image([2], new byte[] { 1 }));

        using var document = FitsDocumentBuilder.Create()
            .Image([2, 3], new short[] { 1, 2, 3, 4, 5, 6 })
            .Build();
        FitsImage image = document.Hdus[0].Image!;

        Assert.Throws<FitsException>(() => image.CopyPhysical(new double[5]));
        Assert.Throws<FitsException>(() => image.CopyPhysical(new double[2, 2]));
        Assert.Throws<FitsException>(() => image.CopyInteger(new short[5]));
    }

    [Fact]
    public void RejectsInvalidBitpixAxesAndTruncatedImageData()
    {
        var invalidBitpix = new FitsFileBuilder()
            .Card("SIMPLE", "T")
            .Card("BITPIX", "12")
            .Card("NAXIS", "0")
            .Build();
        var negativeAxis = new FitsFileBuilder()
            .Card("SIMPLE", "T")
            .Card("BITPIX", "16")
            .Card("NAXIS", "1")
            .Card("NAXIS1", "-1")
            .Build();
        var truncatedData = new FitsFileBuilder()
            .Card("SIMPLE", "T")
            .Card("BITPIX", "32")
            .Card("NAXIS", "1")
            .Card("NAXIS1", "2")
            .Build();

        Assert.Throws<FitsException>(() => FitsDocument.Parse(invalidBitpix));
        Assert.Throws<FitsException>(() => FitsDocument.Parse(negativeAxis));
        Assert.Throws<FitsException>(() => FitsDocument.Parse(truncatedData));
    }

    private static void AssertImageBytes<T>(byte[] expected, int bitpix, T[] values)
        where T : unmanaged
    {
        using var document = CreateImage(bitpix, values);
        byte[] serialized = document.ToArray();
        Assert.Equal(expected, serialized.AsSpan(2880, expected.Length).ToArray());
        Assert.Equal(bitpix, document.Hdus[0].Image!.Bitpix);
    }

    private static FitsDocument CreateImage<T>(int bitpix, T[] values)
        where T : unmanaged
    {
        return bitpix switch
        {
            8 => FitsDocumentBuilder.Create().Image([values.Length], (byte[])(object)values).Build(),
            16 => FitsDocumentBuilder.Create().Image([values.Length], (short[])(object)values).Build(),
            32 => FitsDocumentBuilder.Create().Image([values.Length], (int[])(object)values).Build(),
            64 => FitsDocumentBuilder.Create().Image([values.Length], (long[])(object)values).Build(),
            -32 => FitsDocumentBuilder.Create().Image([values.Length], (float[])(object)values).Build(),
            -64 => FitsDocumentBuilder.Create().Image([values.Length], (double[])(object)values).Build(),
            _ => throw new ArgumentOutOfRangeException(nameof(bitpix)),
        };
    }
}
