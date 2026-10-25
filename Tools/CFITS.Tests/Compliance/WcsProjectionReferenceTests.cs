namespace CFITS.Tests;

public sealed class WcsProjectionReferenceTests
{
    public static IEnumerable<object[]> ReferenceVectors() =>
    [
        ["AZP", 29.869075144010353d, 40.199919184034854d, 45d, 0.5d],
        ["PAR", 29.869092571108126d, 40.190912539698147d, 0d, 0d],
        ["CYP", 29.869075885896336d, 40.199926356615684d, 1d, 1d],
    ];

    [Theory]
    [MemberData(nameof(ReferenceVectors))]
    public void ForwardProjectionMatchesAstropyReference(
        string projection,
        double expectedLongitude,
        double expectedLatitude,
        double pv1,
        double pv2)
    {
        var builder = Header(projection);
        if (projection is "AZP" or "CYP")
        {
            builder.Real("PV2_1", pv1).Real("PV2_2", pv2);
        }

        double[] actual = FitsWorld.FromHeader(builder.Build()).PixelToWorld([2, 3]);

        Assert.InRange(Math.Abs(actual[0] - expectedLongitude), 0, 5e-12);
        Assert.InRange(Math.Abs(actual[1] - expectedLatitude), 0, 5e-12);
    }

    [Fact]
    public void ZpnUsesZeroBasedPolynomialCoefficients()
    {
        double[] actual = FitsWorld.FromHeader(Header("ZPN")
            .Real("PV2_0", 0)
            .Real("PV2_1", 2)
            .Build()).PixelToWorld([2, 3]);

        Assert.InRange(Math.Abs(actual[0] - 29.934633851826174d), 0, 5e-12);
        Assert.InRange(Math.Abs(actual[1] - 40.09998165411082d), 0, 5e-12);
    }

    [Theory]
    [InlineData("AZP", 45d, 0.5d)]
    [InlineData("PAR", 0d, 0d)]
    [InlineData("CYP", 1d, 1d)]
    public void SupportedProjectionsRoundTrip(string projection, double pv1, double pv2)
    {
        var builder = Header(projection);
        if (projection is "AZP" or "CYP")
        {
            builder.Real("PV2_1", pv1).Real("PV2_2", pv2);
        }

        var world = FitsWorld.FromHeader(builder.Build());
        double[] pixel = [2.25, 2.75];
        double[] coordinates = world.PixelToWorld(pixel);
        double[] roundTrip = world.WorldToPixel(coordinates);

        Assert.InRange(Math.Abs(roundTrip[0] - pixel[0]), 0, 5e-11);
        Assert.InRange(Math.Abs(roundTrip[1] - pixel[1]), 0, 5e-11);
    }

    [Fact]
    public void ZpnHigherOrderCoefficientIsEvaluated()
    {
        double[] baseline = FitsWorld.FromHeader(Header("ZPN")
            .Real("PV2_0", 0)
            .Real("PV2_1", 1)
            .Build()).PixelToWorld([2, 3]);
        double[] actual = FitsWorld.FromHeader(Header("ZPN")
            .Real("PV2_0", 0)
            .Real("PV2_1", 1)
            .Real("PV2_2", 0.02)
            .Build()).PixelToWorld([2, 3]);

        Assert.True(Math.Abs(actual[0] - baseline[0]) > 1e-6);
        Assert.True(Math.Abs(actual[1] - baseline[1]) > 1e-6);
    }

    [Fact]
    public void ZpnEvaluatesPolynomialPowersInRadiansBeforeConvertingRadiusToDegrees()
    {
        double[] actual = FitsWorld.FromHeader(Header("ZPN")
            .Real("PV2_0", 0)
            .Real("PV2_1", 1)
            .Real("PV2_2", 0.02)
            .Build()).PixelToWorld([2, 3]);

        Assert.InRange(Math.Abs(actual[0] - 29.869085601555575d), 0, 5e-12);
        Assert.InRange(Math.Abs(actual[1] - 40.1999108607974d), 0, 5e-12);
    }

    private static FitsHeaderBuilder Header(string projection) =>
        FitsHeader.Builder()
            .Integer("WCSAXES", 2)
            .Real("CRPIX1", 1)
            .Real("CRPIX2", 1)
            .Real("CRVAL1", 30)
            .Real("CRVAL2", 40)
            .Real("CDELT1", -0.1)
            .Real("CDELT2", 0.1)
            .Text("CTYPE1", "RA---" + projection)
            .Text("CTYPE2", "DEC--" + projection);
}
