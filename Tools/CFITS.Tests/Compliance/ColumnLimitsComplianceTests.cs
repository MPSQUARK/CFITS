namespace CFITS.Tests;

public sealed class ColumnLimitsComplianceTests
{
    [Fact]
    public void ExposesScalarLimitsOnScalarColumns()
    {
        using var document = FitsDocumentBuilder.Create()
            .BinaryTable(
                new BinaryTableBuilder().Add("VALUE", new[] { 1, 2 }),
                header => header.Integer("TLMIN1", -10).Integer("TLMAX1", 10))
            .Build();

        var column = document.Hdus[1].Table!.Column("VALUE");
        Assert.Equal(-10, column.Minimum);
        Assert.Equal(10, column.Maximum);
    }

    [Fact]
    public void ExposesVectorLimitsOnRepeatedColumns()
    {
        using var document = FitsDocumentBuilder.Create()
            .BinaryTable(
                new BinaryTableBuilder().AddVector("VALUE", 2, new[] { 1, 2 }, [2]),
                header => header.Real("TDMIN1", -1.5).Real("TDMAX1", 4.5))
            .Build();

        var column = document.Hdus[1].Table!.Column("VALUE");
        Assert.Equal(-1.5, column.Minimum);
        Assert.Equal(4.5, column.Maximum);
    }

    [Fact]
    public void RejectsLimitsOnUnsupportedColumnDomains()
    {
        Assert.Throws<FitsException>(() =>
            FitsDocumentBuilder.Create()
                .BinaryTable(
                    new BinaryTableBuilder().Add("VALUE", new[] { true }),
                    header => header.Integer("TLMIN1", 0).Integer("TLMAX1", 1))
                .Build());

        Assert.Throws<FitsException>(() =>
            FitsDocumentBuilder.Create()
                .BinaryTable(
                    new BinaryTableBuilder().Add("VALUE", new[] { 1 }),
                    header => header.Integer("TDMIN1", 0).Integer("TDMAX1", 1))
                .Build());
    }

    [Fact]
    public void RejectsIntegerColumnLimitsWithMinimumAboveMaximum()
    {
        Assert.Throws<FitsException>(() =>
            FitsDocumentBuilder.Create()
                .BinaryTable(
                    new BinaryTableBuilder().Add("VALUE", new[] { 1, 2 }),
                    header => header
                        .Integer("TLMIN1", 10)
                        .Integer("TLMAX1", 2))
                .Build());
    }

    [Fact]
    public void RejectsVectorColumnLimitsWithInconsistentAxisBounds()
    {
        Assert.Throws<FitsException>(() =>
            FitsDocumentBuilder.Create()
                .BinaryTable(
                    new BinaryTableBuilder()
                        .AddVector("VALUE", 2, new[] { 1, 2 }, [2]),
                    header => header
                        .Real("TDMIN1", 0)
                        .Real("TDMAX1", -1))
                .Build());
    }
}
