namespace CFITS.Tests;

public sealed class ValidationRegressionTests
{
    [Fact]
    public void BinaryTextColumnsRejectInvalidWidthsAndRowShapes()
    {
        Assert.Throws<FitsException>(() => new BinaryTableBuilder().Add("TEXT", ["too long"], 3));
        Assert.Throws<FitsException>(() => new BinaryTableBuilder().AddText("TEXT", 3, [["one"], ["two", "three"]]));
        Assert.Throws<FitsException>(() => new BinaryTableBuilder().AddText("TEXT", 0, [["one"]]));
    }

    [Fact]
    public void BinaryBitColumnsRejectMismatchedRows()
    {
        Assert.Throws<FitsException>(() => new BinaryTableBuilder().AddBits("BITS", 3, [[true, false]]));
        Assert.Throws<FitsException>(() => new BinaryTableBuilder().AddBits("BITS", 0, [[]]));
    }

    [Fact]
    public void BinaryBitColumnsRejectCellAllocationOverflow()
    {
        const int bitCount = int.MaxValue - 6;
        const int rowCount = 9;

        Assert.Throws<FitsException>(() =>
            new BinaryTableBuilder().AddBits("BITS", bitCount, new HugeRows(rowCount)));
    }

    [Fact]
    public void BinaryVectorsRequireMatchingDimensionProducts()
    {
        Assert.Throws<FitsException>(() => new BinaryTableBuilder().AddVector("VECTOR", 6, new[] { 1, 2, 3, 4, 5, 6 }, [2, 2]));
        Assert.Throws<FitsException>(() => new BinaryTableBuilder().AddVector("VECTOR", 4, new[] { 1, 2, 3, 4 }, [2, 0]));
    }

    [Fact]
    public void HeaderTextAndComplexValuesUseFiniteAsciiValidation()
    {
        Assert.Throws<FitsException>(() =>
            FitsDocumentBuilder.Create().Image([1], new byte[] { 1 }, header => header.Text("TEXT", "caf\u00E9")).Build());
        Assert.Throws<FitsException>(() =>
            FitsDocumentBuilder.Create().Image([1], new byte[] { 1 }, header => header.Complex("COMPLEX", double.NaN, 1)).Build());
        Assert.Throws<FitsException>(() =>
            FitsDocumentBuilder.Create().Image([1], new byte[] { 1 }, header => header.Complex("COMPLEX", 1, double.PositiveInfinity)).Build());
    }

    private sealed class HugeRows(int count) : IReadOnlyList<bool[]>
    {
        public int Count { get; } = count;

        public bool[] this[int index] => throw new InvalidOperationException("Rows must not be accessed after allocation overflow.");

        public IEnumerator<bool[]> GetEnumerator() => throw new InvalidOperationException("Rows must not be enumerated after allocation overflow.");

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
