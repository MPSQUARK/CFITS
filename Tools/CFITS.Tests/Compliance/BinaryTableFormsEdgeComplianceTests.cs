namespace CFITS.Tests;

public sealed class BinaryTableFormsEdgeComplianceTests
{
    [Fact]
    public void PreservesTdimForFixedVectorsAndAppliesColumnScaling()
    {
        using var document = FitsDocumentBuilder.Create()
            .BinaryTable(
                new BinaryTableBuilder().AddVector("VALUE", 4, [1, 2, 3, 4], [2, 2]),
                header => header.Real("TSCAL1", 0.5).Real("TZERO1", 10))
            .Build();

        FitsColumn column = document.Hdus[1].Table!.Column("VALUE");
        Assert.Equal("4J", column.Format);
        Assert.Equal([2, 2], column.Dimensions!.ToArray());
        Assert.Equal(10.5, column.ReadReal(0, 0));
        Assert.Equal(12, column.ReadReal(0, 3));
    }

    [Fact]
    public void SupportsNullMarkersAtIntegerBoundaries()
    {
        using var document = FitsDocumentBuilder.Create()
            .BinaryTable(
                new BinaryTableBuilder()
                    .Add("BYTE", new byte[] { 0, 255, 1 })
                    .Add("SHORT", new short[] { short.MinValue, 0, short.MaxValue }),
                header => header.Integer("TNULL1", 255).Integer("TNULL2", short.MinValue))
            .Build();

        FitsTable table = document.Hdus[1].Table!;
        Assert.True(table.Column("BYTE").IsNull(1));
        Assert.True(table.Column("SHORT").IsNull(0));
        Assert.Equal(0, table.Column("BYTE").ReadInteger(0));
        Assert.Equal(short.MaxValue, table.Column("SHORT").ReadInteger(2));
    }

}
