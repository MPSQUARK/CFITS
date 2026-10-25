namespace CFITS.Tests;

public sealed class MissingProjectionConventionTests
{
    public static IEnumerable<object[]> Projections() =>
    [
        ["AZP", 29.86907602059247d, 40.199925442923544d],
        ["CEA", 29.869075884724737d, 40.199926965851724d],
        ["AIT", 29.869075282963706d, 40.199926641518019d],
        ["HPX", 29.869134001787188d, 40.169692113324459d],
        ["MER", 29.869075886286851d, 40.199926153538499d],
        ["SFL", 29.8690750878676d, 40.199926558798524d],
        ["PAR", 29.869092571108126d, 40.190912539698147d],
        ["MOL", 29.854622024376908d, 40.179972799439632d],
        ["CYP", 29.869075885896336d, 40.199926356615684d],
        ["SZP", 29.869076020592654d, 40.199925442923259d],
    ];

    [Theory]
    [MemberData(nameof(Projections))]
    public void RepresentativeProjectionMatchesReferenceVector(string projection, double expectedLongitude, double expectedLatitude)
    {
        var builder = FitsHeader.Builder()
            .Integer("WCSAXES", 2)
            .Real("CRPIX1", 1)
            .Real("CRPIX2", 1)
            .Real("CRVAL1", 30)
            .Real("CRVAL2", 40)
            .Real("CDELT1", -0.1)
            .Real("CDELT2", 0.1)
            .Text("CTYPE1", "RA---" + projection)
            .Text("CTYPE2", "DEC--" + projection);

        var world = FitsWorld.FromHeader(builder.Build());
        double[] actual = world.PixelToWorld([2, 3]);

        Assert.Equal(expectedLongitude, actual[0], 10);
        Assert.Equal(expectedLatitude, actual[1], 10);
    }

    [Fact]
    public void AppliesZpnPolynomial()
    {
        var world = FitsWorld.FromHeader(FitsHeader.Builder()
            .Integer("WCSAXES", 2)
            .Real("CRPIX1", 1)
            .Real("CRPIX2", 1)
            .Real("CRVAL1", 30)
            .Real("CRVAL2", 40)
            .Real("CDELT1", -0.1)
            .Real("CDELT2", 0.1)
            .Text("CTYPE1", "RA---ZPN")
            .Text("CTYPE2", "DEC--ZPN")
            .Real("PV2_1", 2)
            .Build());

        double[] actual = world.PixelToWorld([2, 3]);
        Assert.Equal(29.934633851826174, actual[0], 10);
        Assert.Equal(40.09998165411082, actual[1], 10);
    }

    [Fact]
    public void AppliesAzpParameters()
    {
        var world = FitsWorld.FromHeader(FitsHeader.Builder()
            .Integer("WCSAXES", 2)
            .Real("CRPIX1", 1)
            .Real("CRPIX2", 1)
            .Real("CRVAL1", 30)
            .Real("CRVAL2", 40)
            .Real("CDELT1", -0.1)
            .Real("CDELT2", 0.1)
            .Text("CTYPE1", "RA---AZP")
            .Text("CTYPE2", "DEC--AZP")
            .Real("PV2_1", 45)
            .Real("PV2_2", 0.5)
            .Build());

        double[] actual = world.PixelToWorld([2, 3]);
        Assert.Equal(29.869075144010353, actual[0], 10);
        Assert.Equal(40.199919184034854, actual[1], 10);
    }
}
