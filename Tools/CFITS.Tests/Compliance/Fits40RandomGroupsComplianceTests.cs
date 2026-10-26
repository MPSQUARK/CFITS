using System.Text;

namespace CFITS.Tests;

public sealed class Fits40RandomGroupsComplianceTests
{
    [Fact]
    public void ReadsParametersBeforeEachGroupDataArray()
    {
        using var document = FitsDocumentBuilder.Create()
            .RandomGroups(
                bitpix: 16,
                dataAxes: [2],
                parameterCount: 1,
                parameters: [10, 20],
                data: [100, 101, 200, 201],
                parameterNames: ["TIME"])
            .Build();

        FitsHdu hdu = document.Hdus[0];
        FitsGroups groups = hdu.Groups!;

        Assert.Equal(FitsHduKind.RandomGroups, hdu.Kind);
        Assert.Equal(2, groups.Count);
        Assert.Equal(1, groups.ParameterCount);
        Assert.Equal(2, groups.DataLength);
        Assert.Equal("TIME", groups.Names[0]);
        Assert.Equal(10d, groups.ReadParameter(0, 0));
        Assert.Equal(20d, groups.ReadParameter(1, 0));
        Assert.Equal(100d, groups.ReadData(0, 0));
        Assert.Equal(101d, groups.ReadData(0, 1));
        Assert.Equal(200d, groups.ReadData(1, 0));
        Assert.Equal(201d, groups.ReadData(1, 1));
    }

    [Fact]
    public void RejectsRandomGroupsWithANonzeroFirstAxis()
    {
        byte[] file = Header(
            Card("SIMPLE", "= T"),
            Card("BITPIX", "= 16"),
            Card("NAXIS", "= 2"),
            Card("NAXIS1", "= 1"),
            Card("NAXIS2", "= 1"),
            Card("GROUPS", "= T"),
            Card("PCOUNT", "= 1"),
            Card("GCOUNT", "= 1"))
            .Concat(new byte[] { 0, 1, 0, 2 })
            .Concat(Enumerable.Repeat((byte)0, 2876))
            .ToArray();

        var error = Assert.Throws<FitsException>(() => FitsDocument.Parse(file));

        Assert.Contains("NAXIS1", error.Message);
    }

    [Fact]
    public void RejectsGroupMetadataWhenGroupsIsFalse()
    {
        byte[] file = Header(
            Card("SIMPLE", "= T"),
            Card("BITPIX", "= 16"),
            Card("NAXIS", "= 1"),
            Card("NAXIS1", "= 1"),
            Card("GROUPS", "= F"),
            Card("PCOUNT", "= 1"),
            Card("GCOUNT", "= 1"))
            .Concat(new byte[] { 0, 1, 0, 2 })
            .Concat(Enumerable.Repeat((byte)0, 2876))
            .ToArray();

        var error = Assert.Throws<FitsException>(() => FitsDocument.Parse(file));

        Assert.Contains("GROUPS", error.Message);
    }

    [Fact]
    public void RejectsBuilderDataThatDoesNotFillEveryGroup()
    {
        var error = Assert.Throws<FitsException>(() => FitsDocumentBuilder.Create()
            .RandomGroups(
                bitpix: 16,
                dataAxes: [2],
                parameterCount: 1,
                parameters: [10, 20],
                data: [100, 101, 200])
            .Build());

        Assert.Contains("data", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsParametersWhenTheParameterCountIsZero()
    {
        var error = Assert.Throws<FitsException>(() => FitsDocumentBuilder.Create()
            .RandomGroups(
                bitpix: 16,
                dataAxes: [1],
                parameterCount: 0,
                parameters: [7],
                data: [11])
            .Build());

        Assert.Contains("parameter", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsZeroParameterScaling()
    {
        var error = Assert.Throws<FitsException>(() => FitsDocumentBuilder.Create()
            .RandomGroups(
                bitpix: 16,
                dataAxes: [1],
                parameterCount: 1,
                parameters: [7],
                data: [11],
                header: header => header.Real("PSCAL1", 0))
            .Build());

        Assert.Contains("PSCAL1", error.Message);
    }

    private static byte[] Header(params string[] cards)
    {
        byte[] header = Encoding.ASCII.GetBytes(string.Concat(cards.Append(Card("END", ""))));
        return header.Concat(Enumerable.Repeat((byte)' ', 2880 - header.Length)).ToArray();
    }

    private static string Card(string keyword, string value) =>
        (keyword.PadRight(8) + value).PadRight(80);
}
