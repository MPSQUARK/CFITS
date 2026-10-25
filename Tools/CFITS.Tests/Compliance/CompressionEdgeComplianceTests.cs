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
}
