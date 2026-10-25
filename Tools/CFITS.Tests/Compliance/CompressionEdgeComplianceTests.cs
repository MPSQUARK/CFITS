namespace CFITS.Tests;

public sealed class CompressionEdgeComplianceTests
{
    [Fact]
    public void PreservesUndefinedFloatingPixelsInAPartialGzipTile()
    {
        float[] values = [float.NaN, 1.25f, float.PositiveInfinity, -3.5f, 0f, float.NaN, 2f, -1f];
        using var document = FitsDocumentBuilder.Create()
            .GzipImage([4, 2], values)
            .Build();

        FitsImage image = document.Hdus[1].Image!;
        double[] actual = image.ReadPhysical();
        Assert.True(double.IsNaN(actual[0]));
        Assert.Equal(1.25, actual[1]);
        Assert.True(double.IsPositiveInfinity(actual[2]));
        Assert.Equal(-3.5, actual[3]);
        Assert.Equal(0, actual[4]);
        Assert.True(double.IsNaN(actual[5]));
    }
}
