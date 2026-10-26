namespace CFITS.Tests;

public sealed class ExternalTableFixtureTests
{
    [Fact]
    public void ReadsAstropyBinaryTableFixture()
    {
        using var document = FitsDocument.Parse(File.ReadAllBytes(Fixture("astropy-binary-table.fits")));
        var table = document.Hdus[1].Table!;

        Assert.Equal(2, table.RowCount);
        Assert.Equal(-4, table.Column("INTEGER").ReadInteger(0));
        Assert.Equal(7, table.Column("LONG").ReadInteger(1));
        Assert.Equal(new FitsComplex(5, 6), table.Column("DOUBLE_COMPLEX").ReadComplex(0));
        Assert.Equal([1, 0, 0, 0, 0, 0, 0, 1, 1], Enumerable.Range(0, 9)
            .Select(index => table.Column("BITS").ReadInteger(0, index)).ToArray());
        Assert.Equal([2, 2], table.Column("VECTOR").ReadVariableDimensions(0)!);
        Assert.Equal([10L, 20L, 30L, 40L], table.Column("VARIABLE").ReadIntegerVector(0));
    }

    [Fact]
    public void ReadsAstropyAsciiTableFixture()
    {
        using var document = FitsDocument.Parse(File.ReadAllBytes(Fixture("astropy-ascii-table.fits")));
        var table = document.Hdus[1].Table!;

        Assert.True(table.IsAscii);
        Assert.Equal("alpha", table.Column("TEXT").ReadString(0));
        Assert.Equal(-34, table.Column("INTEGER").ReadInteger(1));
        Assert.Equal(1.25, table.Column("FIXED").ReadReal(0), 6);
        Assert.Equal(-0.425, table.Column("EXP").ReadReal(1), 6);
    }

    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "ExternalTables", name);
}
