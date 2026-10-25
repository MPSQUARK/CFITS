namespace CFITS.Tests;

public sealed class TimeReferenceConventionTests
{
    [Fact]
    public void MjdrefIntegerAndFractionAreCombined()
    {
        var clock = FitsClock.FromHeader(FitsHeader.Builder()
            .Integer("MJDREFI", 59000)
            .Real("MJDREFF", 0.125)
            .Text("TIMEUNIT", "d")
            .Build());

        Assert.Equal(59000.125, clock.ReferenceModifiedJulianDate, 12);
        Assert.Equal(59000.125, clock.ToModifiedJulianDate(0), 12);
    }

    [Fact]
    public void MjdrefTakesPrecedenceOverJdrefAndDateref()
    {
        var clock = FitsClock.FromHeader(FitsHeader.Builder()
            .Real("MJDREF", 59000.25)
            .Real("JDREF", 2459000.75)
            .Text("DATEREF", "2000-01-01T00:00:00Z")
            .Build());

        Assert.Equal(59000.25, clock.ReferenceModifiedJulianDate, 12);
    }

    [Fact]
    public void JdrefIntegerAndFractionAreConvertedToModifiedJulianDate()
    {
        var clock = FitsClock.FromHeader(FitsHeader.Builder()
            .Integer("JDREFI", 2459001)
            .Real("JDREFF", 0.25)
            .Build());

        Assert.Equal(59000.75, clock.ReferenceModifiedJulianDate, 12);
    }

    [Fact]
    public void DaterefProvidesTheReferenceWhenNumericReferencesAreAbsent()
    {
        var clock = FitsClock.FromHeader(FitsHeader.Builder()
            .Text("DATEREF", "2020-01-01T12:00:00Z")
            .Build());

        Assert.Equal(58849.5, clock.ReferenceModifiedJulianDate, 12);
    }

    [Fact]
    public void TimerefDeclaresTheReferencePosition()
    {
        var clock = FitsClock.FromHeader(FitsHeader.Builder()
            .Text("TIMEREF", "TOPOCENTER")
            .Build());

        Assert.Equal("TOPOCENTER", clock.ReferencePosition);
    }

    [Fact]
    public void InvalidTimerefIsRejected()
    {
        var error = Assert.Throws<FitsException>(() => FitsClock.FromHeader(
            FitsHeader.Builder().Text("TIMEREF", "MOON").Build()));

        Assert.Equal("TIMEREF", error.Keyword);
    }

    [Fact]
    public void ObserverGeocentricPositionRequiresAllCoordinates()
    {
        var clock = FitsClock.FromHeader(FitsHeader.Builder()
            .Real("OBSGEO-X", 1)
            .Real("OBSGEO-Y", 2)
            .Real("OBSGEO-Z", 3)
            .Build());

        Assert.NotNull(clock.ObserverPosition);
        Assert.Equal((1d, 2d, 3d), clock.ObserverPosition.Value);
        Assert.Throws<FitsException>(() => FitsClock.FromHeader(
            FitsHeader.Builder().Real("OBSGEO-X", 1).Build()));
    }

    [Fact]
    public void NumericReferenceTakesPrecedenceOverDateref()
    {
        var clock = FitsClock.FromHeader(FitsHeader.Builder()
            .Real("JDREF", 2459000.5)
            .Text("DATEREF", "2000-01-01T00:00:00Z")
            .Build());

        Assert.Equal(59000, clock.ReferenceModifiedJulianDate, 12);
    }
}
