using System.IO.Compression;

namespace CFITS.Tests;

public sealed class CompressionEdgeComplianceTests
{
    [Fact]
    public void ReadsUncompressedFallbackTilesAsUnsignedBytes()
    {
        var table = new BinaryTableBuilder()
            .AddVariable("COMPRESSED_DATA", new List<string> { "" })
            .AddVariable("UNCOMPRESSED_DATA", new List<string> { "\xFF\x80\x00\x7F" });

        using var document = FitsDocumentBuilder.Create()
            .BinaryTable(table, header => header
                .Logical("ZIMAGE", true)
                .Text("ZCMPTYPE", "NOCOMPRESS")
                .Integer("ZBITPIX", 8)
                .Integer("ZNAXIS", 1)
                .Integer("ZNAXIS1", 4)
                .Integer("ZTILE1", 4))
            .Build();

        Assert.Equal(new double[] { 255, 128, 0, 127 }, document.Hdus[1].Image!.ReadPhysical());
    }

    [Fact]
    public void RejectsBothImageFallbackColumns()
    {
        var table = new BinaryTableBuilder()
            .AddVariable("COMPRESSED_DATA", new List<string> { "" })
            .AddVariable("GZIP_COMPRESSED_DATA", new List<string> { "" })
            .AddVariable("UNCOMPRESSED_DATA", new List<string> { "x" });

        Assert.Throws<FitsException>(() => FitsDocumentBuilder.Create()
            .BinaryTable(table, header => header
                .Logical("ZIMAGE", true)
                .Text("ZCMPTYPE", "GZIP_1")
                .Integer("ZBITPIX", 8)
                .Integer("ZNAXIS", 1)
                .Integer("ZNAXIS1", 1)
                .Integer("ZTILE1", 1))
            .Build());
    }

    [Fact]
    public void PreservesUndefinedFloatingPixelsInAPartialGzipTile()
    {
        float[] values = [float.NaN, 1.25f, float.PositiveInfinity, -3.5f, 0f, float.NaN, 2f, -1f];
        using var document = FitsDocumentBuilder.Create()
            .GzipImage([4, 2], values)
            .Build();

        FitsImage image = document.Hdus[1].Image!;
        double[] actual = image.ReadPhysical();
        Assert.True(double.IsNaN(actual[0]));
        Assert.Equal(1.25, actual[1]);
        Assert.True(double.IsPositiveInfinity(actual[2]));
        Assert.Equal(-3.5, actual[3]);
        Assert.Equal(0, actual[4]);
        Assert.True(double.IsNaN(actual[5]));
    }

    [Fact]
    public void AppliesOfficialNullPixelMaskToIntegerTiles()
    {
        byte[] pixels = [0, 1, 0, 2, 0, 3, 0, 4];
        byte[] mask = [0, 1, 0, 0];
        var table = new BinaryTableBuilder()
            .AddVariable("COMPRESSED_DATA", new List<string> { Gzip(pixels) })
            .AddVariable("NULL_PIXEL_MASK", new List<string> { Gzip(mask) });

        using var document = FitsDocumentBuilder.Create()
            .BinaryTable(table, header => header
                .Logical("ZIMAGE", true)
                .Text("ZCMPTYPE", "GZIP_1")
                .Text("ZMASKCMP", "GZIP_1")
                .Integer("ZBLANK", -32768)
                .Integer("ZBITPIX", 16)
                .Integer("ZNAXIS", 2)
                .Integer("ZNAXIS1", 2)
                .Integer("ZNAXIS2", 2)
                .Integer("ZTILE1", 2)
                .Integer("ZTILE2", 2))
            .Build();

        double[] actual = document.Hdus[1].Image!.ReadPhysical();
        Assert.Equal(1, actual[0]);
        Assert.True(double.IsNaN(actual[1]));
        Assert.Equal(3, actual[2]);
        Assert.Equal(4, actual[3]);
    }

    [Fact]
    public void AppliesNullPixelMaskAcrossLaterMultiPixelTiles()
    {
        byte[][] pixels =
        [
            [0, 1, 0, 2, 0, 3, 0, 4],
            [0, 5, 0, 6, 0, 7, 0, 8],
        ];
        byte[][] masks =
        [
            [0, 0, 0, 0],
            [0, 0, 0, 1],
        ];
        var table = new BinaryTableBuilder()
            .AddVariable("COMPRESSED_DATA", pixels.Select(Gzip).ToList())
            .AddVariable("NULL_PIXEL_MASK", masks.Select(Gzip).ToList());

        using var document = FitsDocumentBuilder.Create()
            .BinaryTable(table, header => header
                .Logical("ZIMAGE", true)
                .Text("ZCMPTYPE", "GZIP_1")
                .Text("ZMASKCMP", "GZIP_1")
                .Integer("ZBLANK", -32768)
                .Integer("ZBITPIX", 16)
                .Integer("ZNAXIS", 2)
                .Integer("ZNAXIS1", 2)
                .Integer("ZNAXIS2", 4)
                .Integer("ZTILE1", 2)
                .Integer("ZTILE2", 2))
            .Build();

        double[] actual = document.Hdus[1].Image!.ReadPhysical();
        Assert.Equal(1, actual[0]);
        Assert.Equal(2, actual[1]);
        Assert.Equal(3, actual[2]);
        Assert.Equal(4, actual[3]);
        Assert.Equal(5, actual[4]);
        Assert.Equal(6, actual[5]);
        Assert.Equal(7, actual[6]);
        Assert.True(double.IsNaN(actual[7]));
    }

    [Fact]
    public void AcceptsAnEmptyNullPixelMaskTile()
    {
        var table = new BinaryTableBuilder()
            .AddVariable("COMPRESSED_DATA", new List<string> { Gzip([0, 1]) })
            .AddVariable("NULL_PIXEL_MASK", new List<string> { "" });

        using var document = FitsDocumentBuilder.Create()
            .BinaryTable(table, header => header
                .Logical("ZIMAGE", true)
                .Text("ZCMPTYPE", "GZIP_1")
                .Text("ZMASKCMP", "GZIP_1")
                .Integer("ZBLANK", 255)
                .Integer("ZBITPIX", 8)
                .Integer("ZNAXIS", 1)
                .Integer("ZNAXIS1", 2)
                .Integer("ZTILE1", 2))
            .Build();

        Assert.Equal(new double[] { 0, 1 }, document.Hdus[1].Image!.ReadPhysical());
    }

    private static string Gzip(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(bytes);
        }

        return new string(output.ToArray().Select(static value => (char)value).ToArray());
    }
}
