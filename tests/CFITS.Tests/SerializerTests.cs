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

        var document = FitsDocumentBuilder.Create().Image(new[] { pixels.Length }, pixels).Build();
        var destination = ArrayPool<int>.Shared.Rent(pixels.Length);
        document.Hdus[0].Image!.CopyStoredInt32(destination);
        long before = GC.GetAllocatedBytesForCurrentThread();
        document.Hdus[0].Image!.CopyStoredInt32(destination.AsSpan(0, pixels.Length));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 4096, $"Allocated {allocated} bytes.");
        Assert.Equal(pixels[^1], destination[pixels.Length - 1]);
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

public class SerializerTests
{
    [Fact]
    public void SourceGeneratedModelsRoundTrip()
    {
        var exposure = new Exposure
        {
            ExposureSeconds = 12.5,
            Pixels = new float[,] { { 1f, 2f }, { 3f, float.NaN } },
        };
        var path = Path.Combine(Path.GetTempPath(), "cfits-exposure.fits");
        FitsSerializer.Write(path, exposure, AppFitsContext.Default.Exposure);
        var read = FitsSerializer.Read(path, AppFitsContext.Default.Exposure);
        Assert.Equal(12.5, read.ExposureSeconds);
        Assert.Equal(1f, read.Pixels![0, 0]);
        Assert.Equal(2f, read.Pixels[0, 1]);
        Assert.Equal(3f, read.Pixels[1, 0]);
        Assert.True(float.IsNaN(read.Pixels[1, 1]));

        var rows = new List<EventRow> { new() { Time = 1.25, Channel = 7 }, new() { Time = 2.5, Channel = 8 } };
        var table = Path.Combine(Path.GetTempPath(), "cfits-events.fits");
        FitsSerializer.Write(table, rows, AppFitsContext.Default.EventRow);
        var readRows = FitsSerializer.Read(table, AppFitsContext.Default.EventRow);
        Assert.Equal(2, readRows.Count);
        Assert.Equal(1.25, readRows[0].Time);
        Assert.Equal(7, readRows[0].Channel);
        Assert.Equal(8, readRows[1].Channel);
    }
}

[FitsImage]
public sealed class Exposure
{
    [FitsKeyword("EXPTIME")]
    public double ExposureSeconds { get; set; }

    [FitsData]
    public float[,]? Pixels { get; set; }
}

[FitsTable("EVENTS")]
public sealed class EventRow
{
    [FitsColumn("TIME")]
    public double Time { get; set; }

    [FitsColumn("PHA")]
    public int Channel { get; set; }
}

[FitsSerializable(typeof(Exposure))]
[FitsSerializable(typeof(EventRow))]
public sealed partial class AppFitsContext : FitsSerializerContext;
