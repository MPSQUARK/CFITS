using System.Buffers;

namespace CFITS.Tests;

public class ImageTests
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
}
