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
}
