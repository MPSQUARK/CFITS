namespace CFITS.Tests;

public sealed class FixedVectorSchemaComplianceTests
{
    [Fact]
    public void PreservesFixedAndVariableVectorSchemas()
    {
        using var document = FitsDocumentBuilder.Create()
            .BinaryTable(new BinaryTableBuilder()
                .AddVector("FIXED_J", 2, new[] { 1, 2, 3, 4 }, [2])
                .AddVector("FIXED_D", 6, new[] { 1d, 2d, 3d, 4d, 5d, 6d, 7d, 8d, 9d, 10d, 11d, 12d }, [2, 3])
                .AddVariable("VARIABLE_J", new[] { new[] { 7, 8 }, new[] { 9 } })
                .AddVariable("VARIABLE_D", new[] { new[] { 1.5d, 2.5d }, new[] { 3.5d } }))
            .Build();

        var table = document.Hdus[1].Table!;

        Assert.Equal("2J", table.Column("FIXED_J").Format);
        Assert.Equal([2], table.Column("FIXED_J").Dimensions!.ToArray());
        Assert.Equal("6D", table.Column("FIXED_D").Format);
        Assert.Equal([2, 3], table.Column("FIXED_D").Dimensions!.ToArray());
        Assert.Equal("PJ", table.Column("VARIABLE_J").Format);
        Assert.Null(table.Column("VARIABLE_J").Dimensions);
        Assert.Equal("QD", table.Column("VARIABLE_D").Format);
        Assert.Null(table.Column("VARIABLE_D").Dimensions);
    }
}
