namespace CFITS.Tests;

public sealed class WcsSectionEightNineGapTests
{
    [Fact]
    public void ExposesCelestialSpectralAndTimeAxisMetadata()
    {
        var world = FitsWorld.FromHeader(FitsHeader.Builder()
            .Integer("WCSAXES", 3)
            .Text("CTYPE1", "RA---TAN")
            .Text("CUNIT1", "deg")
            .Text("CTYPE2", "DEC--TAN")
            .Text("CUNIT2", "deg")
            .Text("CTYPE3", "FREQ")
            .Text("CUNIT3", "Hz")
            .Build());

        Assert.Equal("RA---TAN", world.AxisMetadata[0].Type);
        Assert.True(world.AxisMetadata[0].IsCelestial);
        Assert.True(world.AxisMetadata[2].IsSpectral);
        Assert.Equal("Hz", world.AxisMetadata[2].Unit);
    }

    [Fact]
    public void AlternateAxisDescriptionFallsBackToPrimaryAxisCountAndMetadata()
    {
        var header = FitsHeader.Builder()
            .Integer("WCSAXES", 2)
            .Text("CTYPE1", "RA---TAN")
            .Text("CTYPE2", "DEC--TAN")
            .Text("CTYPE1A", "RA---SIN")
            .Text("CTYPE2A", "DEC--SIN")
            .Build();

        var world = FitsWorld.FromHeader(header, 'A');

        Assert.Equal(2, world.Axes);
        Assert.Equal("RA---SIN", world.AxisMetadata[0].Type);
    }

    [Fact]
    public void RejectsProjectionAlgorithmOnNonCelestialAxis()
    {
        var error = Assert.Throws<FitsException>(() => FitsWorld.FromHeader(FitsHeader.Builder()
            .Integer("WCSAXES", 1)
            .Text("CTYPE1", "FREQ-TAN")
            .Build()));

        Assert.Contains("celestial", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AxisTimeMetadataOverridesGlobalTimeMetadata()
    {
        var clock = FitsClock.FromHeader(FitsHeader.Builder()
            .Text("TIMESYS", "UTC")
            .Text("TIMEUNIT", "s")
            .Text("CUNIT1", "min")
            .Real("MJDREF", 59000)
            .Build(), 1);

        Assert.Equal(1d / 1440d, clock.UnitDays, 15);
    }

    [Fact]
    public void RejectsInvalidTimeUnitAndNonfiniteTimeError()
    {
        Assert.Throws<FitsException>(() => FitsClock.FromHeader(
            FitsHeader.Builder().Text("TIMEUNIT", "fortnight").Build()));
        Assert.Throws<FitsException>(() => FitsClock.FromHeader(
            FitsHeader.Builder().Real("TIMEDEL", double.NaN).Build()));
    }
}
