namespace CFITS.Tests;

public sealed class FitsColumnBulkTests
{
    [Fact]
    public void CopiesFixedBinaryScalarsWithStoredAndPhysicalValues()
    {
        using var physical = Open(
            new BinaryTableBuilder()
                .Add("B", new byte[] { 1, 2 })
                .Add("I", new short[] { 3, 4 })
                .Add("J", new[] { 5, 6 })
                .Add("K", new long[] { 7, 8 })
                .Add("E", new[] { 1f, 2f })
                .Add("D", new[] { 3d, 4d }),
            header => header
                .Real("TSCAL5", 2).Real("TZERO5", 10)
                .Real("TSCAL6", 2).Real("TZERO6", 10));
        FitsTable table = physical.Hdus[1].Table!;

        AssertFixedInteger(table, "B", new long[] { 1, 2 });
        AssertFixedInteger(table, "I", new long[] { 3, 4 });
        AssertFixedInteger(table, "J", new long[] { 5, 6 });
        AssertFixedInteger(table, "K", new long[] { 7, 8 });

        var reals = new double[2];
        Assert.True(table.Column("E").TryCopyReal(reals));
        Assert.Equal(new[] { 12d, 14d }, reals);
        Assert.True(table.Column("D").TryCopyReal(reals));
        Assert.Equal(new[] { 16d, 18d }, reals);

        using var stored = Open(
            new BinaryTableBuilder().Add("E", new[] { 1f, 2f }),
            header => header.Real("TSCAL1", 2).Real("TZERO1", 10),
            FitsScaleMode.Stored);
        Assert.True(stored.Hdus[1].Table!.Column("E").TryCopyReal(reals));
        Assert.Equal(new[] { 1d, 2d }, reals);
    }

    [Fact]
    public void RejectsIntegerNullMarkersOnRealColumns()
    {
        Assert.Throws<FitsException>(() => Open(
            new BinaryTableBuilder().Add("VALUE", new[] { 7f, 8f }),
            header => header.Integer("TNULL1", 7)));
    }

    [Fact]
    public void CopiesIntoTypedDestinationsWithoutWidening()
    {
        using var document = Open(
            new BinaryTableBuilder()
                .Add("I", new short[] { 3, 4 })
                .Add("J", new[] { 5, 6 })
                .Add("B", new byte[] { 7, 8 })
                .Add("E", new[] { 1f, 2f }));
        FitsTable table = document.Hdus[1].Table!;

        var integers = new short[2];
        var intValues = new int[2];
        var byteValues = new byte[2];
        var reals = new float[2];
        Assert.True(table.Column("I").TryCopyInteger(integers));
        Assert.True(table.Column("J").TryCopyInteger(intValues));
        Assert.True(table.Column("B").TryCopyInteger(byteValues));
        Assert.True(table.Column("E").TryCopyReal(reals));
        Assert.Equal(new short[] { 3, 4 }, integers);
        Assert.Equal(new[] { 5, 6 }, intValues);
        Assert.Equal(new byte[] { 7, 8 }, byteValues);
        Assert.Equal(new[] { 1f, 2f }, reals);
    }

    [Fact]
    public void ComparesInt64NullMarkersBeforeConvertingToReal()
    {
        long marker = (1L << 60) + 3;
        long[] values = [marker - 1, marker, marker + 1];
        using var document = Open(
            new BinaryTableBuilder().Add("VALUE", values),
            header => header.Integer("TNULL1", marker));

        var result = new double[values.Length];
        Assert.True(document.Hdus[1].Table!.Column("VALUE").TryCopyReal(result));

        Assert.Equal((double)values[0], result[0]);
        Assert.True(double.IsNaN(result[1]));
        Assert.Equal((double)values[2], result[2]);
    }

    [Fact]
    public void PreservesNullsDestinationValidationAndFallback()
    {
        using var document = Open(
            new BinaryTableBuilder().Add("VALUE", new[] { int.MinValue, 4 }),
            header => header.Integer("TNULL1", int.MinValue));
        FitsColumn column = document.Hdus[1].Table!.Column("VALUE");

        var real = new double[2];
        Assert.True(column.TryCopyReal(real));
        Assert.True(double.IsNaN(real[0]));
        Assert.Equal(4d, real[1]);

        var integer = new long[2];
        var exception = Assert.Throws<FitsException>(() => column.TryCopyInteger(integer));
        Assert.Contains("row 0 is null", exception.Message);
        Assert.Throws<FitsException>(() => column.TryCopyReal(new double[1]));

        using var fallback = Open(
            new BinaryTableBuilder().AddVector("VECTOR", 2, new[] { 1, 2 }, new[] { 2 }));
        FitsColumn vector = fallback.Hdus[1].Table!.Column("VECTOR");
        Assert.False(vector.TryCopyReal(new double[1]));
        Assert.Equal(1d, vector.ReadReal(0, 0));
    }

    [Fact]
    public void CopiesVariableInt32RowsInOnePass()
    {
        int[][] rows = [[1, 2, 3, 4], [5, -6], []];
        using var document = Open(new BinaryTableBuilder().AddVariable("VALUES", rows));
        FitsColumn column = document.Hdus[1].Table!.Column("VALUES");

        for (int row = 0; row < rows.Length; row++)
        {
            var actual = new int[rows[row].Length];
            column.CopyIntegers(row, actual);
            Assert.Equal(rows[row], actual);
        }
    }

    [Fact]
    public void CopyIntegersHonorsNulls()
    {
        using var document = Open(
            new BinaryTableBuilder().Add("VALUES", new[] { 1, 99 }),
            header => header.Integer("TNULL1", 99));
        FitsColumn column = document.Hdus[1].Table!.Column("VALUES");
        var present = new int[1];
        column.CopyIntegers(0, present);
        Assert.Equal(1, present[0]);
        Assert.Throws<FitsException>(() => column.CopyIntegers(1, present));
    }

    private static FitsDocument Open(
        BinaryTableBuilder table,
        Action<FitsHeaderBuilder>? header = null,
        FitsScaleMode mode = FitsScaleMode.Physical)
    {
        using var stream = new MemoryStream();
        using var document = FitsDocumentBuilder.Create().BinaryTable(table, header).Build();
        document.Write(stream);
        stream.Position = 0;
        return FitsDocument.Open(stream, new FitsOpenOptions { ScaleMode = mode });
    }

    private static void AssertFixedInteger(FitsTable table, string name, long[] expected)
    {
        var actual = new long[expected.Length];
        Assert.True(table.Column(name).TryCopyInteger(actual));
        Assert.Equal(expected, actual);
    }
}
