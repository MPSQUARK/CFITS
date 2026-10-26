namespace CFITS.Tests;

public sealed class TableAndGroupsRemainingGapTests
{
    [Fact]
    public void ReadsEveryBitInAFixedBitColumn()
    {
        using var document = FitsDocumentBuilder.Create()
            .BinaryTable(new BinaryTableBuilder().AddBits("FLAGS", 9, [[true, false, true, false, false, false, false, false, true]]))
            .Build();

        FitsColumn column = document.Hdus[1].Table!.Column("FLAGS");

        Assert.Equal(1, column.ReadInteger(0, 0));
        Assert.Equal(0, column.ReadInteger(0, 1));
        Assert.Equal(1, column.ReadInteger(0, 2));
        Assert.Equal(1, column.ReadInteger(0, 8));
    }

    [Fact]
    public void ReadsDoublePrecisionComplexValues()
    {
        using var document = FitsDocumentBuilder.Create()
            .BinaryTable(new BinaryTableBuilder().AddComplex("VALUE", [new FitsComplex(1.25, -2.5)]))
            .Build();

        FitsComplex value = document.Hdus[1].Table!.Column("VALUE").ReadComplex(0);

        Assert.Equal(1.25, value.Real);
        Assert.Equal(-2.5, value.Imaginary);
    }

    [Fact]
    public void SupportsZeroRandomGroupsWithNoParametersOrData()
    {
        using var document = FitsDocumentBuilder.Create()
            .RandomGroups(16, [2], 0, [], [])
            .Build();

        FitsGroups groups = document.Hdus[0].Groups!;
        Assert.Equal(0, groups.Count);
        Assert.Equal(0, groups.ParameterCount);
        Assert.Equal(2, groups.DataLength);
    }

    [Fact]
    public void SupportsZeroRandomGroupsWithDeclaredParametersButNoRecords()
    {
        using var document = FitsDocumentBuilder.Create()
            .RandomGroups(16, [2], 2, [], [])
            .Build();

        FitsGroups groups = document.Hdus[0].Groups!;
        Assert.Equal(0, groups.Count);
        Assert.Equal(2, groups.ParameterCount);
        Assert.Equal(2, groups.DataLength);
        Assert.Equal(["P1", "P2"], groups.Names);
    }

    [Fact]
    public void RejectsAnInvalidBinaryTableBitpix()
    {
        Assert.Throws<FitsException>(() =>
            FitsDocumentBuilder.Create()
                .BinaryTable(
                    new BinaryTableBuilder().Add("VALUE", new[] { 1 }),
                    header => header.Integer("BITPIX", 16))
                .Build());
    }

    [Fact]
    public void RejectsRandomGroupKeywordsPastTheParameterCount()
    {
        Assert.Throws<FitsException>(() =>
            FitsDocumentBuilder.Create()
                .RandomGroups(
                    16,
                    [1],
                    1,
                    [7],
                    [11],
                    header: header => header.Real("PSCAL2", 2))
                .Build());
    }
}
