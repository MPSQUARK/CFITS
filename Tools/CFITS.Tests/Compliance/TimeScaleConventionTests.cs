namespace CFITS.Tests;

public sealed class TimeScaleConventionTests
{
    [Fact]
    public void UtcToTaiUsesTheLeapSecondOffsetAtTheReferenceEpoch()
    {
        double utc = FitsClock.ModifiedJulianDate("2017-01-01T00:00:00Z");

        double tai = FitsClock.ConvertModifiedJulianDate(utc, "UTC", "TAI");

        Assert.Equal(utc + (37d / 86400d), tai, 12);
    }

    [Fact]
    public void TaiToUtcUsesTheLeapSecondOffsetAtTheReferenceEpoch()
    {
        double utc = FitsClock.ModifiedJulianDate("2017-01-01T00:00:00Z");
        double tai = utc + (37d / 86400d);

        Assert.Equal(utc, FitsClock.ConvertModifiedJulianDate(tai, "TAI", "UTC"), 12);
    }

    [Fact]
    public void Ut1AndUtcConversionUsesEarthRotationAtTheReferenceEpoch()
    {
        double utc = FitsClock.ModifiedJulianDate("2020-01-01T00:00:00Z");
        const double ut1MinusUtcSeconds = -0.177;

        double ut1 = FitsClock.ConvertModifiedJulianDate(utc, "UTC", "UT1");

        Assert.Equal(utc + (ut1MinusUtcSeconds / 86400d), ut1, 9);
    }

    [Fact]
    public void LeapSecondTimestampIsRepresentable()
    {
        double leapSecond = FitsClock.ModifiedJulianDate("2016-12-31T23:59:60Z");

        Assert.Equal(57754d, leapSecond, 12);
    }

    [Fact]
    public void Ut1UsesTheDeterministicVectorForItsEpoch()
    {
        double utc = FitsClock.ModifiedJulianDate("2000-01-01T00:00:00Z");

        Assert.Equal(utc + (0.355d / 86400d), FitsClock.ConvertModifiedJulianDate(utc, "UTC", "UT1"), 12);
    }

    [Fact]
    public void Ut1RejectsEpochsWithoutAnObservedVector()
    {
        double utc = FitsClock.ModifiedJulianDate("1970-01-01T00:00:00Z");

        Assert.Throws<FitsException>(() => FitsClock.ConvertModifiedJulianDate(utc, "UTC", "UT1"));
    }

    [Fact]
    public void InvalidLeapSecondIsRejected()
    {
        Assert.Throws<FitsException>(() => FitsClock.ModifiedJulianDate("2015-12-31T23:59:60Z"));
    }

    [Fact]
    public void TdbAndTcbRequireAnExternalEphemeris()
    {
        var error = Assert.Throws<FitsException>(() => FitsClock.FromHeader(
            FitsHeader.Builder().Text("TIMESYS", "TDB").Build()));

        Assert.Contains("external ephemeris", error.Message);
        Assert.Equal("TIMESYS", error.Keyword);
        Assert.Throws<FitsException>(() => FitsClock.ConvertModifiedJulianDate(59000, "TCB", "TT"));
    }
}
