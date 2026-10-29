namespace CFITS.Tests;

public sealed class GeneratedFloatVectorTableComplianceTests
{
    [Fact]
    public void GeneratedSerializerRoundTripsFloatVectorColumns()
    {
        var rows = new[]
        {
            new FloatVectorRow { Samples = [1.25f, 2.5f] },
            new FloatVectorRow { Samples = [3.75f] },
        };

        using var stream = new MemoryStream();
        FitsSerializer.Write(stream, new FloatVectorFile { Table = new FloatVectorTable { Rows = rows } });
        stream.Position = 0;

        IReadOnlyList<FloatVectorRow> result =
            FitsSerializer.Read<FloatVectorFile>(stream).Table.Rows;

        Assert.Equal(rows.Length, result.Count);
        for (int index = 0; index < rows.Length; index++)
        {
            Assert.Equal(rows[index].Samples, result[index].Samples);
        }
    }
}
