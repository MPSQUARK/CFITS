using System.Buffers;

namespace CFITS.Tests;

public sealed class ImageTests
{
    [Fact]
    public void CopyingAMillionPixelsDoesNotAllocatePerPixel()
    {
        var pixels = new int[1_000_000];
        for (int index = 0; index < pixels.Length; index++)
        {
            pixels[index] = index;
        }

        using var document = FitsDocumentBuilder.Create().Image(new[] { pixels.Length }, pixels).Build();
        var destination = ArrayPool<int>.Shared.Rent(pixels.Length);
        try
        {
            document.Hdus[0].Image!.CopyStoredInt32(destination);
            long before = GC.GetAllocatedBytesForCurrentThread();
            document.Hdus[0].Image!.CopyStoredInt32(destination.AsSpan(0, pixels.Length));
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(allocated < 4096, $"Allocated {allocated} bytes.");
            Assert.Equal(pixels[^1], destination[pixels.Length - 1]);
        }
        finally
        {
            ArrayPool<int>.Shared.Return(destination);
        }
    }

    [Fact]
    public void SixtyFourBitPixelsStayExactPastTheFloatMantissa()
    {
        long awkward = (1L << 60) + 3;
        long[] signed = [awkward, long.MinValue, long.MaxValue];
        using var signedDocument = FitsDocumentBuilder.Create().Image(new[] { signed.Length }, signed).Build();
        var signedImage = signedDocument.Hdus[0].Image!;
        Assert.Equal((Int128)awkward, signedImage.ReadInteger(0));
        Assert.Equal((Int128)long.MinValue, signedImage.ReadInteger(1));
        Assert.Equal((Int128)long.MaxValue, signedImage.ReadInteger(2));

        long[] stored = [long.MinValue, -1, 0, long.MaxValue];
        using var unsigned = FitsDocumentBuilder.Create()
            .Image(new[] { stored.Length }, stored, header => header.Real("BZERO", 9223372036854775808d))
            .Build();
        var image = unsigned.Hdus[0].Image!;
        Assert.Equal(Int128.Zero, image.ReadInteger(0));
        Assert.Equal(((Int128)1 << 63) - 1, image.ReadInteger(1));
        Assert.Equal((Int128)1 << 63, image.ReadInteger(2));
        Assert.Equal(((Int128)1 << 64) - 1, image.ReadInteger(3));

        long[] columnValues = [long.MinValue, awkward, long.MaxValue];
        using var table = FitsDocumentBuilder.Create()
            .BinaryTable(new BinaryTableBuilder().Add("K", columnValues))
            .Build();
        var column = table.Hdus[1].Table!.Column("K");
        Assert.Equal((Int128)awkward, column.ReadExactInteger(1));
        Assert.Equal((Int128)long.MaxValue, column.ReadExactInteger(2));
    }

    [Fact]
    public void FloatBulkCopyMatchesPhysicalScalarReads()
    {
        using var byteDocument = FitsDocumentBuilder.Create()
            .Image([2], new byte[] { 0, 255 }, header => header.Integer("BLANK", 255))
            .Build();
        AssertBulkMatchesScalar(byteDocument.Hdus[0].Image!);

        using var integerDocument = FitsDocumentBuilder.Create()
            .Image([4], new short[] { -2, -1, 0, 3 }, header => header
                .Real("BSCALE", 2.5)
                .Real("BZERO", 10)
                .Integer("BLANK", -1))
            .Build();
        AssertBulkMatchesScalar(integerDocument.Hdus[0].Image!);

        using var intDocument = FitsDocumentBuilder.Create()
            .Image([2], new[] { int.MinValue, int.MaxValue })
            .Build();
        AssertBulkMatchesScalar(intDocument.Hdus[0].Image!);

        using var longDocument = FitsDocumentBuilder.Create()
            .Image([2], new[] { long.MinValue, long.MaxValue })
            .Build();
        AssertBulkMatchesScalar(longDocument.Hdus[0].Image!);

        using var floatingDocument = FitsDocumentBuilder.Create()
            .Image([4], new[] { float.NaN, float.PositiveInfinity, -1.25f, 2.5f })
            .Build();
        AssertBulkMatchesScalar(floatingDocument.Hdus[0].Image!);

        using var doubleDocument = FitsDocumentBuilder.Create()
            .Image([3], new[] { double.NaN, double.NegativeInfinity, 1.5 })
            .Build();
        AssertBulkMatchesScalar(doubleDocument.Hdus[0].Image!);
    }

    [Fact]
    public void EveryBitpixBulkCopyMatchesPhysicalScalarReads()
    {
        using var byteDocument = FitsDocumentBuilder.Create()
            .Image([3], new byte[] { 0, 255, 7 }, header => header
                .Real("BSCALE", 2.5)
                .Real("BZERO", 10)
                .Integer("BLANK", 255))
            .Build();
        using var shortDocument = FitsDocumentBuilder.Create()
            .Image([3], new short[] { -2, -1, 3 }, header => header
                .Real("BSCALE", 2.5)
                .Real("BZERO", 10)
                .Integer("BLANK", -1))
            .Build();
        using var intDocument = FitsDocumentBuilder.Create()
            .Image([3], new[] { int.MinValue, 0, int.MaxValue }, header => header.Real("BSCALE", 2.5).Real("BZERO", 10))
            .Build();
        using var longDocument = FitsDocumentBuilder.Create()
            .Image([3], new[] { (1L << 60) + 3, long.MinValue, long.MaxValue }, header => header.Real("BSCALE", 2.5).Real("BZERO", 10))
            .Build();
        using var floatDocument = FitsDocumentBuilder.Create()
            .Image([3], new[] { float.NaN, -1.25f, float.PositiveInfinity }, header => header.Real("BSCALE", 2.5).Real("BZERO", 10))
            .Build();
        using var doubleDocument = FitsDocumentBuilder.Create()
            .Image([3], new[] { double.NaN, -1.25, double.PositiveInfinity }, header => header.Real("BSCALE", 2.5).Real("BZERO", 10))
            .Build();

        AssertDoubleBulkMatchesScalar(byteDocument.Hdus[0].Image!);
        AssertDoubleBulkMatchesScalar(shortDocument.Hdus[0].Image!);
        AssertDoubleBulkMatchesScalar(intDocument.Hdus[0].Image!);
        AssertDoubleBulkMatchesScalar(longDocument.Hdus[0].Image!);
        AssertDoubleBulkMatchesScalar(floatDocument.Hdus[0].Image!);
        AssertDoubleBulkMatchesScalar(doubleDocument.Hdus[0].Image!);
    }

    [Fact]
    public void FloatBulkCopyValidatesDestinationLength()
    {
        using var document = FitsDocumentBuilder.Create().Image([2], new short[] { 1, 2 }).Build();
        FitsImage image = document.Hdus[0].Image!;

        Assert.Throws<FitsException>(() => image.CopyPhysical(Array.Empty<float>()));
        Assert.Throws<FitsException>(() => image.CopyPhysical(new float[1]));
    }

    [Fact]
    public void IntegerBulkCopiesPreserveExactValues()
    {
        using var document = FitsDocumentBuilder.Create()
            .Image([3], new short[] { -2, 0, 7 })
            .Build();
        FitsImage image = document.Hdus[0].Image!;

        var bytes = new byte[3];
        var shorts = new short[3];
        var integers = new int[3];
        var longs = new long[3];
        image.CopyInteger(bytes);
        image.CopyInteger(shorts);
        image.CopyInteger(integers);
        image.CopyInteger(longs);

        Assert.Equal(new byte[] { 254, 0, 7 }, bytes);
        Assert.Equal(new short[] { -2, 0, 7 }, shorts);
        Assert.Equal(new[] { -2, 0, 7 }, integers);
        Assert.Equal(new long[] { -2, 0, 7 }, longs);
    }

    [Fact]
    public void RectangularFloatCopiesMatchScalarReadsAndAxisOrder()
    {
        using var document = FitsDocumentBuilder.Create()
            .Image([3, 2], new short[] { 1, 2, 3, 4, 5, 6 }, header => header
                .Real("BSCALE", 2)
                .Real("BZERO", 10)
                .Integer("BLANK", 5))
            .Build();
        FitsImage image = document.Hdus[0].Image!;
        var values = new float[2, 3];
        image.CopyPhysical(values);

        for (int y = 0; y < values.GetLength(0); y++)
        {
            for (int x = 0; x < values.GetLength(1); x++)
            {
                Assert.Equal((float)image.ReadPhysical(x + (y * 3)), values[y, x]);
            }
        }
        Assert.True(float.IsNaN(values[1, 1]));
    }

    [Fact]
    public void RectangularDoubleCopiesMatchScalarReadsForRankThree()
    {
        using var document = FitsDocumentBuilder.Create()
            .Image([2, 3, 2], new double[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 })
            .Build();
        FitsImage image = document.Hdus[0].Image!;
        var values = new double[2, 3, 2];
        image.CopyPhysical(values);

        for (int z = 0; z < values.GetLength(0); z++)
        {
            for (int y = 0; y < values.GetLength(1); y++)
            {
                for (int x = 0; x < values.GetLength(2); x++)
                {
                    Assert.Equal(image.ReadPhysical(x + (2 * (y + (3 * z)))), values[z, y, x]);
                }
            }
        }
    }

    [Fact]
    public void RectangularCopiesValidateRankAndShape()
    {
        using var document = FitsDocumentBuilder.Create().Image([3, 2], new float[6]).Build();
        FitsImage image = document.Hdus[0].Image!;

        Assert.Throws<FitsException>(() => image.CopyPhysical(new float[3, 2, 1]));
        Assert.Throws<FitsException>(() => image.CopyPhysical(new float[2, 2]));
    }

    private static void AssertBulkMatchesScalar(FitsImage image)
    {
        var values = new float[image.Length];
        image.CopyPhysical(values);
        for (int index = 0; index < image.Length; index++)
        {
            Assert.Equal((float)image.ReadPhysical(index), values[index]);
        }
    }

    private static void AssertDoubleBulkMatchesScalar(FitsImage image)
    {
        var values = new double[image.Length];
        image.CopyPhysical(values);
        for (int index = 0; index < image.Length; index++)
        {
            double expected = image.ReadPhysical(index);
            if (double.IsNaN(expected))
            {
                Assert.True(double.IsNaN(values[index]));
            }
            else
            {
                Assert.Equal(expected, values[index]);
            }
        }
    }
}
