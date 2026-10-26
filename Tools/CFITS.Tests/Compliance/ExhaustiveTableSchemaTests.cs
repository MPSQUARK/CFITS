using System.Buffers.Binary;

namespace CFITS.Tests;

public sealed class ExhaustiveTableSchemaTests
{
    [Fact]
    public void WritesAndReadsEveryBinarySchemaThroughRawCells()
    {
        byte[] row = new byte[2 + 3 + 4 + 4 + 8 + 16 + 8];
        BinaryPrimitives.WriteInt32BigEndian(row.AsSpan(37), 4);
        var table = new FitsRawTableBuilder(false)
            .AddColumn("LOGICAL", "2L")
            .AddColumn("BITS", "17X")
            .AddColumn("TEXT", "4A2")
            .AddColumn("SHORT", "2I", tdim: "(2)")
            .AddColumn("SINGLE_COMPLEX", "C")
            .AddColumn("DOUBLE_COMPLEX", "M")
            .AddColumn("VARIABLE", "1PJ(4)")
            .AddRow(row)
            .SetHeap(new byte[] { 0, 0, 0, 1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0, 4 })
            .Build();

        var columns = table.Hdus[1].Table!.Columns;
        Assert.Equal(["2L", "17X", "4A2", "2I", "C", "M", "1PJ(4)"],
            columns.Select(static column => column.Format).ToArray());
        Assert.Equal(4, columns[6].Count(0));
        Assert.Equal(new byte[] { 0, 0, 0, 1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0, 4 }, columns[6].ReadVariableBytes(0));
    }

    [Fact]
    public void WritesAsciiSchemasWithExplicitGapsAndPrecisionExponent()
    {
        var table = new FitsRawTableBuilder(true)
            .AddColumn("INTEGER", "I5", 1)
            .AddColumn("REAL", "E12.5E2", 9)
            .AddColumn("TEXT", "A4", 24)
            .AddRow(new byte[27])
            .Build();

        Assert.Equal(27, table.Hdus[1].Header.GetInt64("NAXIS1"));
        Assert.Equal("E12.5E2", table.Hdus[1].Table!.Column("REAL").Format);
    }

    [Fact]
    public void ExposesVariableVectorsAndRowDimensions()
    {
        byte[] row = new byte[8];
        BinaryPrimitives.WriteInt32BigEndian(row, 4);
        var table = new FitsRawTableBuilder(false)
            .AddColumn("VALUES", "1PJ", tdim: "(2,2)")
            .AddRow(row)
            .SetHeap(new byte[] { 0, 0, 0, 1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0, 4 })
            .Build();

        var column = table.Hdus[1].Table!.Column("VALUES");
        Assert.Equal([1L, 2L, 3L, 4L], column.ReadIntegerVector(0));
        Assert.Equal(new[] { 2, 2 }, column.ReadVariableDimensions(0)!);
    }

    [Fact]
    public void RejectsMalformedRawRowsAndOverlappingAsciiColumns()
    {
        Assert.Throws<FitsException>(() => new FitsRawTableBuilder(false)
            .AddColumn("VALUE", "1J")
            .AddRow(new byte[3])
            .Build());

        Assert.Throws<FitsException>(() => new FitsRawTableBuilder(true)
            .AddColumn("LEFT", "A5", 1)
            .AddColumn("RIGHT", "A5", 3)
            .AddRow(new byte[7])
            .Build());
    }
}
