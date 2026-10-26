namespace CFITS.Tests;

public sealed class CompressionDirectiveComplianceTests
{
    [Fact]
    public void RejectsUnknownImageCompressionDirective()
    {
        var table = new BinaryTableBuilder()
            .AddVariable("COMPRESSED_DATA", new List<string> { "" });

        Assert.Throws<FitsException>(() => FitsDocumentBuilder.Create()
            .BinaryTable(table, header => header
                .Logical("ZIMAGE", true)
                .Text("ZCMPTYPE", "NOT_AN_OFFICIAL_ALGORITHM")
                .Integer("ZBITPIX", 16)
                .Integer("ZNAXIS", 1)
                .Integer("ZNAXIS1", 1)
                .Integer("ZTILE1", 1))
            .Build());
    }

    [Fact]
    public void RejectsNonPositiveTileDimensionsAndMismatchedTileRows()
    {
        var table = new BinaryTableBuilder()
            .AddVariable("COMPRESSED_DATA", new List<string> { "" });

        Assert.Throws<FitsException>(() => FitsDocumentBuilder.Create()
            .BinaryTable(table, header => header
                .Logical("ZIMAGE", true)
                .Text("ZCMPTYPE", "GZIP_1")
                .Integer("ZBITPIX", 16)
                .Integer("ZNAXIS", 2)
                .Integer("ZNAXIS1", 3)
                .Integer("ZNAXIS2", 3)
                .Integer("ZTILE1", 0)
                .Integer("ZTILE2", 2))
            .Build());

        Assert.Throws<FitsException>(() => FitsDocumentBuilder.Create()
            .BinaryTable(table, header => header
                .Logical("ZIMAGE", true)
                .Text("ZCMPTYPE", "GZIP_1")
                .Integer("ZBITPIX", 16)
                .Integer("ZNAXIS", 2)
                .Integer("ZNAXIS1", 3)
                .Integer("ZNAXIS2", 3)
                .Integer("ZTILE1", 2)
                .Integer("ZTILE2", 2))
            .Build());
    }

    [Fact]
    public void RejectsUnsupportedCompressedTableAlgorithm()
    {
        var table = new BinaryTableBuilder()
            .AddVariable("VALUES", new List<string> { "" });

        Assert.Throws<FitsException>(() => FitsDocumentBuilder.Create()
            .BinaryTable(table, header => header
                .Logical("ZTABLE", true)
                .Integer("ZNAXIS1", 4)
                .Integer("ZNAXIS2", 1)
                .Integer("ZPCOUNT", 0)
                .Integer("ZTILELEN", 1)
                .Text("ZFORM1", "1J")
                .Text("ZCTYP1", "HCOMPRESS_1"))
            .Build());
    }

    [Fact]
    public void RejectsHcompressForNonTwoDimensionalImages()
    {
        var table = new BinaryTableBuilder()
            .AddVariable("COMPRESSED_DATA", new List<string> { "" });

        Assert.Throws<FitsException>(() => FitsDocumentBuilder.Create()
            .BinaryTable(table, header => header
                .Logical("ZIMAGE", true)
                .Text("ZCMPTYPE", "HCOMPRESS_1")
                .Integer("ZBITPIX", 32)
                .Integer("ZNAXIS", 1)
                .Integer("ZNAXIS1", 1)
                .Integer("ZTILE1", 1))
            .Build());
    }

    [Fact]
    public void RejectsUnknownFloatingPointQuantizationDirective()
    {
        var table = new BinaryTableBuilder()
            .AddVariable("COMPRESSED_DATA", new List<string> { "" });

        Assert.Throws<FitsException>(() => FitsDocumentBuilder.Create()
            .BinaryTable(table, header => header
                .Logical("ZIMAGE", true)
                .Text("ZCMPTYPE", "GZIP_1")
                .Text("ZQUANTIZ", "SUBTRACTIVE DITHER 3")
                .Integer("ZBITPIX", -32)
                .Integer("ZNAXIS", 2)
                .Integer("ZNAXIS1", 1)
                .Integer("ZNAXIS2", 1)
                .Integer("ZTILE1", 1)
                .Integer("ZTILE2", 1))
            .Build());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10001)]
    public void RejectsOutOfRangeDitherSeeds(int seed)
    {
        var table = new BinaryTableBuilder()
            .AddVariable("COMPRESSED_DATA", new List<string> { "" });

        Assert.Throws<FitsException>(() => FitsDocumentBuilder.Create()
            .BinaryTable(table, header => header
                .Logical("ZIMAGE", true)
                .Text("ZCMPTYPE", "GZIP_1")
                .Text("ZQUANTIZ", "SUBTRACTIVE_DITHER_1")
                .Integer("ZDITHER0", seed)
                .Integer("ZBITPIX", -32)
                .Integer("ZNAXIS", 1)
                .Integer("ZNAXIS1", 1)
                .Integer("ZTILE1", 1))
            .Build());
    }

    [Fact]
    public void RequiresScalingColumnsAsACompletePair()
    {
        var table = new BinaryTableBuilder()
            .AddVariable("COMPRESSED_DATA", new List<string> { "" })
            .AddVariable("ZSCALE", new List<string> { "" });

        Assert.Throws<FitsException>(() => FitsDocumentBuilder.Create()
            .BinaryTable(table, header => header
                .Logical("ZIMAGE", true)
                .Text("ZCMPTYPE", "GZIP_1")
                .Integer("ZBITPIX", -32)
                .Integer("ZNAXIS", 1)
                .Integer("ZNAXIS1", 1)
                .Integer("ZTILE1", 1))
            .Build());
    }

    [Fact]
    public void RestoresVariableColumnsFromAnUncompressedDirectiveBundle()
    {
        byte[] bundle = new byte[20];
        bundle[3] = 4;
        bundle[7] = 16;
        bundle[11] = 1;
        bundle[19] = 1;
        var payload = new string(bundle.Select(static value => (char)value).ToArray());
        var table = new BinaryTableBuilder()
            .AddVariable("VALUES", new List<string> { payload });

        using var document = FitsDocumentBuilder.Create()
            .BinaryTable(table, header => header
                .Logical("ZTABLE", true)
                .Integer("ZNAXIS1", 8)
                .Integer("ZNAXIS2", 1)
                .Integer("ZPCOUNT", 20)
                .Integer("ZTILELEN", 1)
                .Text("ZFORM1", "1PJ")
                .Text("ZCTYP1", "NOCOMPRESS"))
            .Build();

        FitsColumn values = document.Hdus[1].Table!.Column("VALUES");
        Assert.Equal(1, values.Count(0));
        Assert.Equal(1, values.ReadInteger(0));
    }

    [Fact]
    public void RestoresPDescriptorsAndTableMetadata()
    {
        byte[] bundle =
        [
            0, 0, 0, 4, 0, 0, 0, 0,
            0, 0, 0, 1, 0, 0, 0, 1
        ];
        var payload = new string(bundle.Select(static value => (char)value).ToArray());
        var table = new BinaryTableBuilder()
            .AddVariable("VALUES", new List<string> { payload });

        using var document = FitsDocumentBuilder.Create()
            .BinaryTable(table, header => header
                .Logical("ZTABLE", true)
                .Integer("ZNAXIS1", 8)
                .Integer("ZNAXIS2", 1)
                .Integer("ZPCOUNT", bundle.Length)
                .Integer("ZTILELEN", 1)
                .Text("ZFORM1", "1PJ")
                .Text("ZCTYP1", "NOCOMPRESS")
                .Text("TUNIT1", "adu"))
            .Build();

        FitsTable restored = document.Hdus[1].Table!;
        Assert.Equal("adu", document.Hdus[1].Header.GetString("TUNIT1"));
        Assert.Equal(1, restored.Column("VALUES").Count(0));
    }
}
