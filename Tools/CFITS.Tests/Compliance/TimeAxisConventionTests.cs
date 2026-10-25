namespace CFITS.Tests;

public sealed class TimeAxisConventionTests
{
    [Fact]
    public void TimeAxisUsesOneBasedReferencePixelAndDeclaredUnit()
    {
        var world = FitsWorld.FromHeader(FitsHeader.Builder()
            .Integer("WCSAXES", 1)
            .Real("CRPIX1", 1)
            .Real("CRVAL1", 100)
            .Real("CDELT1", 2)
            .Text("CTYPE1", "TIME")
            .Text("CUNIT1", "s")
            .Build());

        Assert.Equal(100, world.PixelToWorld([1])[0], 12);
        Assert.Equal(102, world.PixelToWorld([2])[0], 12);
        Assert.Equal([1d], world.WorldToPixel([100]));
        Assert.Equal([2d], world.WorldToPixel([102]));
    }

    [Fact]
    public void TimeAxisReferenceCanBeCombinedWithMjdrefAndTimeOffset()
    {
        var clock = FitsClock.FromHeader(FitsHeader.Builder()
            .Text("TIMESYS", "TT")
            .Text("TIMEUNIT", "s")
            .Integer("MJDREFI", 59000)
            .Real("MJDREFF", 0.5)
            .Real("TIMEOFFS", 2)
            .Text("CTYPE1", "TIME")
            .Text("CUNIT1", "s")
            .Build());

        Assert.Equal(59000.5 + (12d / 86400d), clock.ToModifiedJulianDate(10), 12);
    }

    [Fact]
    public void TimeMetadataUsesDeclaredUnitResolutionAndReferencePixel()
    {
        var clock = FitsClock.FromHeader(FitsHeader.Builder()
            .Text("TIMEUNIT", "min")
            .Real("TIMEDEL", 2)
            .Real("TIMEPIXR", 0)
            .Build());

        Assert.Equal(1d / 1440d, clock.UnitDays, 15);
        Assert.Equal(2, clock.Resolution, 12);
        Assert.Equal(0, clock.PixelReference, 12);
        Assert.Equal(-1, clock.AtPixelReference(0), 12);
    }

    [Fact]
    public void NegativeTimeMetadataIsRejected()
    {
        Assert.Throws<FitsException>(() => FitsClock.FromHeader(
            FitsHeader.Builder().Real("TIMEDEL", -1).Build()));
        Assert.Throws<FitsException>(() => FitsClock.FromHeader(
            FitsHeader.Builder().Real("TIMEPIXR", 1.1).Build()));
    }

    [Fact]
    public void UnknownTimeScaleIsRejectedAtHeaderBoundary()
    {
        var error = Assert.Throws<FitsException>(() => FitsClock.FromHeader(
            FitsHeader.Builder().Text("TIMESYS", "MARS").Build()));

        Assert.Equal("TIMESYS", error.Keyword);
    }
}
