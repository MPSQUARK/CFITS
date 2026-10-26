namespace CFITS.Tests;

public sealed class WcsSectionEightNineReferenceVectorTests
{
    [Fact]
    public void AlternateDescriptionFallsBackPerKeywordToThePrimaryWcs()
    {
        var world = FitsWorld.FromHeader(FitsHeader.Builder()
            .Integer("WCSAXES", 2)
            .Text("CTYPE1", "RA---TAN")
            .Text("CTYPE2", "DEC--TAN")
            .Text("CUNIT1", "deg")
            .Real("CRPIX1", 1)
            .Real("CRPIX2", 1)
            .Real("CRVAL1", 30)
            .Real("CRVAL2", 40)
            .Real("CDELT1", -0.1)
            .Real("CDELT2", 0.1)
            .Text("CTYPE1A", "RA---SIN")
            .Text("CTYPE2A", "DEC--SIN")
            .Real("CRPIX1A", 1)
            .Build(), 'A');

        Assert.Equal("deg", world.AxisMetadata[0].Unit);
        Assert.Equal("RA---SIN", world.AxisMetadata[0].Type);
        // Astropy 8.0.2/WCSLIB reference vector; evaluated independently outside CFITS.
        double[] actual = world.PixelToWorld([2, 3]);
        Assert.Equal(29.869075020612463, actual[0], 10);
        Assert.Equal(40.199926964882287, actual[1], 10);
    }

    [Theory]
    [InlineData("ENER")]
    [InlineData("WAVE")]
    [InlineData("AWAV")]
    [InlineData("VELO")]
    [InlineData("VRAD")]
    [InlineData("VOPT")]
    [InlineData("BETA")]
    [InlineData("ZOPT")]
    public void RecognizesEveryPaperThreeSpectralAxisPrefix(string prefix)
    {
        var world = FitsWorld.FromHeader(FitsHeader.Builder()
            .Integer("WCSAXES", 1)
            .Text("CTYPE1", prefix)
            .Text("CUNIT1", "Hz")
            .Build());

        Assert.True(world.AxisMetadata[0].IsSpectral);
    }

    [Fact]
    public void RejectsInvalidProjectionParameterAtHeaderBoundary()
    {
        var error = Assert.Throws<FitsException>(() => FitsWorld.FromHeader(FitsHeader.Builder()
            .Integer("WCSAXES", 2)
            .Text("CTYPE1", "RA---CEA")
            .Text("CTYPE2", "DEC--CEA")
            .Real("PV2_1", 0)
            .Build()));

        Assert.Contains("PV2_1", error.Message);
    }

    [Fact]
    public void AxisSpecificTimeReferenceOverridesGlobalReference()
    {
        var clock = FitsClock.FromHeader(FitsHeader.Builder()
            .Text("TIMESYS", "UTC")
            .Text("TIMEUNIT", "s")
            .Text("CUNIT1", "min")
            .Real("MJDREF", 59000)
            .Real("TIMEOFFS", 10)
            .Real("TIMEDEL", 3)
            .Real("TIMEPIXR", 0)
            .Build(), 1);

        Assert.Equal("UTC", clock.Scale);
        Assert.Equal(59000, clock.ReferenceModifiedJulianDate, 12);
        Assert.Equal(1d / 1440d, clock.UnitDays, 15);
        Assert.Equal(3, clock.Resolution, 12);
        Assert.Equal(0, clock.PixelReference, 12);
        Assert.Equal(59000 + 13 / 1440d, clock.ToModifiedJulianDate(3), 12);
    }

    [Fact]
    public void ExposesTimeErrorAndBinningMetadata()
    {
        var clock = FitsClock.FromHeader(FitsHeader.Builder()
            .Real("TIMEDEL", 2)
            .Real("TIMEPIXR", 0.25)
            .Real("TIMSYER", 0.001)
            .Real("TIERRELA", 0.002)
            .Real("TIERABSO", 0.003)
            .Build());

        Assert.Equal(0.001, clock.ScaleError, 12);
        Assert.Equal(0.002, clock.RelativeError, 12);
        Assert.Equal(0.003, clock.AbsoluteError, 12);
        Assert.Equal(2, clock.BinWidth, 12);
        Assert.Equal(0.25, clock.BinReferencePixel, 12);
    }

    [Fact]
    public void UsesVersionedExternalEarthOrientationAndEphemerisProviders()
    {
        var options = new FitsClockOptions(new FixedEarthOrientation(), new FixedEphemeris());
        double utc = 59000;

        Assert.Equal(utc + (0.125 / 86400d), FitsClock.ConvertModifiedJulianDate(utc, "UTC", "UT1", options), 12);
        Assert.Equal(59000 + (1.5 / 86400d), FitsClock.ConvertModifiedJulianDate(59000, "TT", "TDB", options), 12);
    }

    [Fact]
    public void HeaderTimeScaleUsesConfiguredEphemerisProvider()
    {
        var clock = FitsClock.FromHeader(
            FitsHeader.Builder()
                .Text("TIMESYS", "TDB")
                .Text("TIMEUNIT", "d")
                .Real("MJDREF", 59000)
                .Build(),
            new FitsClockOptions(Ephemeris: new FixedEphemeris()));

        Assert.Equal("TDB", clock.Scale);
        Assert.Equal(59000 - (1.5 / 86400d), clock.ToModifiedJulianDate(0, "TT"), 12);
    }

    [Fact]
    public void HeaderTimeScaleRejectsTdbWithoutAnEphemerisProvider()
    {
        var error = Assert.Throws<FitsException>(() => FitsClock.FromHeader(
            FitsHeader.Builder().Text("TIMESYS", "TDB").Build()));

        Assert.Contains("ephemeris", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FixedEarthOrientation : IEarthOrientationProvider
    {
        public string Version => "IERS-test-vector-2026a";

        public bool TryGetUt1MinusUtc(double utcModifiedJulianDate, out double seconds)
        {
            seconds = 0.125;
            return true;
        }
    }

    private sealed class FixedEphemeris : IEphemerisProvider
    {
        public string Version => "JPL-test-vector-2026a";

        public bool TryGetTdbMinusTt(double terrestrialTimeModifiedJulianDate, out double seconds)
        {
            seconds = 1.5;
            return true;
        }

        public bool TryGetTcbMinusTdb(double barycentricDynamicalTimeModifiedJulianDate, out double seconds)
        {
            seconds = 0;
            return true;
        }
    }
}
