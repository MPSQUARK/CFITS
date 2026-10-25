using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace CFITS.Tests;

public sealed class TableSpecTests
{
    [Fact]
    public void EncodesAsciiFieldsAtDeclaredStartsWithSpecFormats()
    {
        using var document = FitsDocumentBuilder.Create()
            .AsciiTable(new AsciiTableBuilder()
                .AddText("NAME", ["ab", "longer"], 6)
                .AddInteger("COUNT", [7, -12], 5)
                .AddReal("FLUX", [1.25, -0.5], 10, 2, 'E')
                .AddReal("VALUE", [12.5, -3.25], 8, 2, 'F'))
            .Build();

        var table = document.Hdus[1].Table!;
        Assert.True(table.IsAscii);
        Assert.Equal(2, table.RowCount);
        Assert.Equal("A6", table.Column("NAME").Format);
        Assert.Equal("I5", table.Column("COUNT").Format);
        Assert.Equal("E10.2", table.Column("FLUX").Format);
        Assert.Equal("F8.2", table.Column("VALUE").Format);
        Assert.Equal("ab", table.Column("NAME").ReadString(0));
        Assert.Equal(-12, table.Column("COUNT").ReadInteger(1));
        Assert.Equal(1.25, table.Column("FLUX").ReadReal(0), 10);
        Assert.Equal(-3.25, table.Column("VALUE").ReadReal(1), 10);

        Assert.Equal(29, document.Hdus[1].Header.GetInt64("NAXIS1"));
        Assert.Equal(1, document.Hdus[1].Header.GetInt64("TBCOL1"));
        Assert.Equal(7, document.Hdus[1].Header.GetInt64("TBCOL2"));
        Assert.Equal(12, document.Hdus[1].Header.GetInt64("TBCOL3"));
        Assert.Equal(22, document.Hdus[1].Header.GetInt64("TBCOL4"));

        Assert.Equal(
            Encoding.ASCII.GetBytes(
                "ab    " + "    7" + "  1.25E+00" + "   12.50"
                + "longer" + "  -12" + " -5.00E-01" + "   -3.25"),
            document.Hdus[1].RawData.ToArray());
        Assert.Equal(
            Encoding.ASCII.GetBytes("longer" + "  -12" + " -5.00E-01" + "   -3.25"),
            document.Hdus[1].RawData.Span.Slice(29, 29).ToArray());
    }

    [Fact]
    public void EncodesBinaryScalarTypesInBigEndianRowStride()
    {
        using var document = FitsDocumentBuilder.Create()
            .BinaryTable(new BinaryTableBuilder()
                .Add("B", new byte[] { 1, 2 })
                .Add("I", new short[] { -3, 4 })
                .Add("J", new[] { 5, -6 })
                .Add("K", new long[] { 7, -8 })
                .Add("E", new[] { 1.5f, -2.5f })
                .Add("D", new[] { 3.25, -4.5 }))
            .Build();

        var table = document.Hdus[1].Table!;
        Assert.Equal(["B", "I", "J", "K", "E", "D"], table.Columns.Select(column => column.Format).ToArray());
        Assert.Equal(27, document.Hdus[1].Header.GetInt64("NAXIS1"));
        Assert.Equal(54, document.Hdus[1].RawData.Length);

        byte[] expected = new byte[27];
        expected[0] = 1;
        BinaryPrimitives.WriteInt16BigEndian(expected.AsSpan(1), -3);
        BinaryPrimitives.WriteInt32BigEndian(expected.AsSpan(3), 5);
        BinaryPrimitives.WriteInt64BigEndian(expected.AsSpan(7), 7);
        BinaryPrimitives.WriteSingleBigEndian(expected.AsSpan(15), 1.5f);
        BinaryPrimitives.WriteDoubleBigEndian(expected.AsSpan(19), 3.25);
        Assert.Equal(expected, document.Hdus[1].RawData.Span[..27].ToArray());

        Assert.Equal(2, table.Column("B").ReadInteger(1));
        Assert.Equal(-6, table.Column("J").ReadInteger(1));
        Assert.Equal(-4.5, table.Column("D").ReadReal(1));
    }

    [Fact]
    public void PreservesVectorsComplexAndBitPackingRules()
    {
        using var document = FitsDocumentBuilder.Create()
            .BinaryTable(new BinaryTableBuilder()
                .AddVector("V", 4, new[] { 1, 2, 3, 4, 5, 6, 7, 8 }, [2, 2])
                .AddComplex("M", [new FitsComplex(1, -2), new FitsComplex(3, 4)])
                .AddSingleComplex("C", [new FitsComplex(1.5, -2.5), new FitsComplex(3.5, 4.5)])
                .AddBits("X", 10, [[true, false, false, false, false, false, false, true, true, false],
                                   [false, true, false, false, false, false, false, false, false, true]]))
            .Build();

        var table = document.Hdus[1].Table!;
        Assert.Equal("4J", table.Column("V").Format);
        Assert.NotNull(table.Column("V").Dimensions);
        Assert.Equal([2, 2], table.Column("V").Dimensions!);
        Assert.Equal(4, table.Column("V").Count(0));
        Assert.Equal(4, table.Column("V").ReadInteger(0, 3));
        Assert.Equal(8, table.Column("V").ReadInteger(1, 3));
        Assert.Equal(new FitsComplex(3, 4), table.Column("M").ReadComplex(1));
        Assert.Equal(1.5, table.Column("C").ReadComplex(0).Real);
        Assert.Equal(-2.5, table.Column("C").ReadComplex(0).Imaginary);
        Assert.Equal(1, table.Column("X").ReadInteger(0, 0));
        Assert.Equal(1, table.Column("X").ReadInteger(0, 7));
        Assert.Equal(1, table.Column("X").ReadInteger(0, 8));
        Assert.Equal(1, table.Column("X").ReadInteger(1, 1));
        Assert.Equal(1, table.Column("X").ReadInteger(1, 9));
        Assert.Equal(0x81, document.Hdus[1].RawData.Span[40]);
        Assert.Equal(0x40, document.Hdus[1].RawData.Span[82]);
    }

    [Fact]
    public void AppliesScaleZeroAndNullMarkersWithoutChangingStoredBytes()
    {
        using var document = FitsDocumentBuilder.Create()
            .BinaryTable(
                new BinaryTableBuilder()
                    .Add("VALUE", new[] { 2, -99, 4 })
                    .Add("REAL", new[] { 1.5f, 2.5f, 3.5f }),
                header => header
                    .Real("TSCAL1", 2)
                    .Real("TZERO1", 10)
                    .Integer("TNULL1", -99))
            .Build();

        var value = document.Hdus[1].Table!.Column("VALUE");
        Assert.Equal(2, value.Scale);
        Assert.Equal(2, value.ReadInteger(0));
        Assert.Equal(14, value.ReadReal(0));
        Assert.True(value.IsNull(1));
        Assert.Throws<FitsException>(() => value.ReadInteger(1));
        Assert.Throws<FitsException>(() => value.ReadExactInteger(0));
        Assert.Throws<FitsException>(() => value.ReadExactInteger(1));
        Assert.Equal(-99, BinaryPrimitives.ReadInt32BigEndian(document.Hdus[1].RawData.Span[8..12]));
    }

    [Fact]
    public void WritesVariableDescriptorsAndHeapOffsetsIndependently()
    {
        using var document = FitsDocumentBuilder.Create()
            .BinaryTable(new BinaryTableBuilder()
                .AddVariable("INTS", new int[][] { [10, 20], [30], [] })
                .AddVariable("DOUBLES", new[] { new[] { 1.5, 2.5 }, new[] { 3.5 }, Array.Empty<double>() })
                .AddVariableBits("BITS", new[] { new[] { true, false, true }, Array.Empty<bool>(), new[] { false, true, true, true, false, false, false, false, true } }))
            .Build();

        var hdu = document.Hdus[1];
        Assert.Equal(32, hdu.Header.GetInt64("NAXIS1"));
        Assert.Equal(39, hdu.Header.GetInt64("PCOUNT"));
        Assert.Equal(135, hdu.RawData.Length);

        var raw = hdu.RawData.Span;
        Assert.Equal(2, BinaryPrimitives.ReadInt32BigEndian(raw));
        Assert.Equal(0, BinaryPrimitives.ReadInt32BigEndian(raw[4..]));
        Assert.Equal(2, BinaryPrimitives.ReadInt64BigEndian(raw[8..]));
        Assert.Equal(12, BinaryPrimitives.ReadInt64BigEndian(raw[16..]));
        Assert.Equal(3, BinaryPrimitives.ReadInt32BigEndian(raw[24..]));
        Assert.Equal(36, BinaryPrimitives.ReadInt32BigEndian(raw[28..]));
        Assert.Equal(1, BinaryPrimitives.ReadInt32BigEndian(raw[32..]));
        Assert.Equal(8, BinaryPrimitives.ReadInt32BigEndian(raw[36..]));
        Assert.Equal(1, BinaryPrimitives.ReadInt64BigEndian(raw[40..]));
        Assert.Equal(28, BinaryPrimitives.ReadInt64BigEndian(raw[48..]));
        Assert.Equal(0, BinaryPrimitives.ReadInt32BigEndian(raw[56..]));
        Assert.Equal(37, BinaryPrimitives.ReadInt32BigEndian(raw[60..]));
        Assert.Equal(0, BinaryPrimitives.ReadInt32BigEndian(raw[64..]));
        Assert.Equal(12, BinaryPrimitives.ReadInt32BigEndian(raw[68..]));
        Assert.Equal(0, BinaryPrimitives.ReadInt64BigEndian(raw[72..]));
        Assert.Equal(36, BinaryPrimitives.ReadInt64BigEndian(raw[80..]));
        Assert.Equal(9, BinaryPrimitives.ReadInt32BigEndian(raw[88..]));
        Assert.Equal(37, BinaryPrimitives.ReadInt32BigEndian(raw[92..]));

        var table = hdu.Table!;
        Assert.Equal([10, 20], [table.Column("INTS").ReadInteger(0), table.Column("INTS").ReadInteger(0, 1)]);
        Assert.Equal(30, table.Column("INTS").ReadInteger(1));
        Assert.Equal(0, table.Column("INTS").Count(2));
        Assert.Equal(1.5, table.Column("DOUBLES").ReadReal(0), 10);
        Assert.Equal(3.5, table.Column("DOUBLES").ReadReal(1), 10);
        Assert.Equal(9, table.Column("BITS").Count(2));
        Assert.Equal(1, table.Column("BITS").ReadInteger(2, 1));
    }

    [Fact]
    public void RejectsMalformedFixedAndVariableTableInputs()
    {
        Assert.Throws<FitsException>(() => new BinaryTableBuilder()
            .AddVector("BAD", 3, new[] { 1, 2, 3 }, [2]));
        Assert.Throws<FitsException>(() => new BinaryTableBuilder()
            .AddBits("BAD", 3, [[true, false]]));
        Assert.Throws<FitsException>(() => new BinaryTableBuilder()
            .AddText("BAD", 2, new[] { new[] { "ok" }, new[] { "too", "many" } }));
        Assert.Throws<FitsException>(() => new BinaryTableBuilder()
            .AddText("BAD", 2, new[] { new[] { "too-wide" } }));
    }
}
