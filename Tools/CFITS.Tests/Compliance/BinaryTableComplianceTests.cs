using System.Buffers.Binary;

namespace CFITS.Tests;

public sealed class BinaryTableComplianceTests
{
    [Fact]
    public void PreservesBinaryLayoutsForAllScalarAndVectorCodes()
    {
        using var document = FitsDocumentBuilder.Create()
            .BinaryTable(new BinaryTableBuilder()
                .Add("LOGICAL", new bool?[] { true, null })
                .Add("BYTE", new byte[] { 255, 1 })
                .Add("SHORT", new short[] { -2, 3 })
                .Add("INTEGER", new[] { -4, 5 })
                .Add("LONG", new long[] { -6, 7 })
                .Add("SINGLE", new[] { 1.5f, -2.5f })
                .Add("DOUBLE", new[] { 3.5, -4.5 })
                .AddSingleComplex("COMPLEX", [new FitsComplex(1, 2), new FitsComplex(3, 4)])
                .AddComplex("DOUBLE_COMPLEX", [new FitsComplex(5, 6), new FitsComplex(7, 8)])
                .AddBits("BITS", 9, [[true, false, false, false, false, false, false, true, true],
                                      [false, true, false, false, false, false, false, false, false]]))
            .Build();

        var table = document.Hdus[1].Table!;
        Assert.True(table.Column("LOGICAL").ReadLogical(0));
        Assert.True(table.Column("LOGICAL").IsNull(1));
        Assert.Equal(255, table.Column("BYTE").ReadInteger(0));
        Assert.Equal(-2, table.Column("SHORT").ReadInteger(0));
        Assert.Equal(-4, table.Column("INTEGER").ReadInteger(0));
        Assert.Equal(-6, table.Column("LONG").ReadInteger(0));
        Assert.Equal(1.5, table.Column("SINGLE").ReadReal(0), 6);
        Assert.Equal(-4.5, table.Column("DOUBLE").ReadReal(1), 6);
        Assert.Equal(new FitsComplex(1, 2), table.Column("COMPLEX").ReadComplex(0));
        Assert.Equal(new FitsComplex(7, 8), table.Column("DOUBLE_COMPLEX").ReadComplex(1));
        Assert.Equal(1, table.Column("BITS").ReadInteger(0, 8));
    }

    [Fact]
    public void RejectsVariableDescriptorsOutsideDeclaredHeap()
    {
        using var source = FitsDocumentBuilder.Create()
            .BinaryTable(new BinaryTableBuilder().AddVariable("VALUES", new[] { new[] { 1, 2 } }))
            .Build();
        byte[] bytes = source.ToArray();
        int pcountCard = bytes.AsSpan().IndexOf("PCOUNT"u8);
        Assert.True(pcountCard >= 0);
        bytes.AsSpan(pcountCard + 10, 20).Fill((byte)' ');
        bytes[pcountCard + 29] = (byte)'0';

        Assert.Throws<FitsException>(() => FitsDocument.Parse(bytes));
    }

    [Fact]
    public void RejectsVariableDescriptorsOutsideTheHeapDuringParsing()
    {
        using var source = FitsDocumentBuilder.Create()
            .BinaryTable(new BinaryTableBuilder().AddVariable("VALUES", new[] { new[] { 1 } }))
            .Build();
        byte[] bytes = source.ToArray();
        const int extensionDataOffset = 5760;
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(extensionDataOffset), 2);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(extensionDataOffset + 4), 4);

        Assert.Throws<FitsException>(() => FitsDocument.Parse(bytes));
    }

    [Fact]
    public void AllowsEmptyAndExactEndVariableArrays()
    {
        using var document = FitsDocumentBuilder.Create()
            .BinaryTable(new BinaryTableBuilder().AddVariable("VALUES", new[] { Array.Empty<int>(), new[] { 1, 2 } }))
            .Build();

        var column = document.Hdus[1].Table!.Column("VALUES");
        Assert.Equal(0, column.Count(0));
        Assert.Equal(2, column.Count(1));
        Assert.Equal(2, column.ReadInteger(1, 1));
    }

    [Fact]
    public void RejectsNullKeywordOnFloatingPointColumn()
    {
        Assert.Throws<FitsException>(() =>
            FitsDocumentBuilder.Create()
                .BinaryTable(
                    new BinaryTableBuilder().Add("VALUE", new[] { 1.5f }),
                    header => header.Integer("TNULL1", 0))
                .Build());
    }

    [Fact]
    public void RejectsScalingKeywordOnLogicalColumn()
    {
        Assert.Throws<FitsException>(() =>
            FitsDocumentBuilder.Create()
                .BinaryTable(
                    new BinaryTableBuilder().Add("FLAG", new[] { true }),
                    header => header.Real("TSCAL1", 2))
                .Build());
    }
}
