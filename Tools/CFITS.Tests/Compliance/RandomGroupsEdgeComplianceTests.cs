namespace CFITS.Tests;

public sealed class RandomGroupsEdgeComplianceTests
{
    [Fact]
    public void RejectsGroupDataThatCannotFillTheDeclaredShape()
    {
        var error = Assert.Throws<FitsException>(() => FitsDocumentBuilder.Create()
            .RandomGroups(16, [2], 1, [7], [11])
            .Build());
        Assert.Contains("data", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SupportsGroupsWithoutParametersAndAppliesDataScaling()
    {
        using var document = FitsDocumentBuilder.Create()
            .RandomGroups(
                16,
                [2],
                0,
                Array.Empty<double>(),
                [-2, 3, 7, 8],
                parameterNames: null,
                header: header => header.Real("BSCALE", 2).Real("BZERO", 10))
            .Build();

        FitsGroups groups = document.Hdus[0].Groups!;
        Assert.Equal(2, groups.Count);
        Assert.Equal(0, groups.ParameterCount);
        Assert.Equal(2, groups.DataLength);
        Assert.Equal(6, groups.ReadData(0, 0));
        Assert.Equal(16, groups.ReadData(0, 1));
        Assert.Throws<FitsException>(() => groups.ReadData(2, 0));
        Assert.Throws<FitsException>(() => groups.ReadData(0, 2));
    }

    [Fact]
    public void AppliesIndependentParameterScalingAtTheGroupBoundary()
    {
        using var document = FitsDocumentBuilder.Create()
            .RandomGroups(
                16,
                [1],
                1,
                [4, -3],
                [10, 20],
                parameterNames: null,
                header: header => header
                    .Real("PSCAL1", 2)
                    .Real("PZERO1", 100))
            .Build();

        FitsGroups groups = document.Hdus[0].Groups!;
        Assert.Equal(2, groups.Count);
        Assert.Equal(108, groups.ReadParameter(0, 0));
        Assert.Equal(94, groups.ReadParameter(1, 0));
        Assert.Equal(10, groups.ReadData(0, 0));
        Assert.Equal(20, groups.ReadData(1, 0));
    }
}
