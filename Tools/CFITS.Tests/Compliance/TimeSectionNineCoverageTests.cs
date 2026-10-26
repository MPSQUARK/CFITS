namespace CFITS.Tests;

public sealed class TimeSectionNineCoverageTests
{
    [Fact]
    public void AxisMetadataOverridesGlobalMetadataForEveryTimeField()
    {
        var clock = FitsClock.FromHeader(FitsHeader.Builder()
            .Text("TIMESYS", "TT")
            .Text("TIMESYS1", "TAI")
            .Text("TIMEUNIT", "s")
            .Text("CUNIT1", "min")
            .Real("MJDREF", 59000)
            .Real("MJDREF1", 59001)
            .Real("TIMEOFFS", 2)
            .Real("TIMEDEL", 4)
            .Real("TIMEDEL1", 5)
            .Real("TIMEPIXR", 0.25)
            .Real("TIMSYER", 0.1)
            .Real("TIERRELA", 0.3)
            .Real("TIERABSO", 0.5)
            .Text("TIMEREF", "GEOCENTER")
            .Text("TIMEREF1", "TOPOCENTER")
            .Build(), 1);

        Assert.Equal("TAI", clock.Scale);
        Assert.Equal(59001, clock.ReferenceModifiedJulianDate, 12);
        Assert.Equal(1d / 1440d, clock.UnitDays, 15);
        Assert.Equal(5, clock.Resolution, 12);
        Assert.Equal(0.25, clock.PixelReference, 12);
        Assert.Equal(0.1, clock.ScaleError, 12);
        Assert.Equal(0.3, clock.RelativeError, 12);
        Assert.Equal(0.5, clock.AbsoluteError, 12);
        Assert.Equal("TOPOCENTER", clock.ReferencePosition);
        Assert.Equal(59001 + (7d / 1440d), clock.ToModifiedJulianDate(5), 12);
    }

    [Fact]
    public void TimeOffsetFallsBackToTimezeroAndPreservesAxisPrecedence()
    {
        var clock = FitsClock.FromHeader(FitsHeader.Builder()
            .Text("TIMEUNIT", "s")
            .Real("MJDREF", 59000)
            .Real("TIMEZERO", 10)
            .Build(), 1);

        Assert.Equal(59000 + (10d / 86400d), clock.ToModifiedJulianDate(0), 12);
    }

    [Fact]
    public void ExposesObservationBoundsElapsedTimeAndEphemerisReference()
    {
        var clock = FitsClock.FromHeader(FitsHeader.Builder()
            .Real("TSTART", 10)
            .Real("TSTOP", 20)
            .Real("TELAPSE", 10)
            .Real("XPOSURE", 9)
            .Text("PLEPHEM", "JPL-DE440")
            .Build());

        Assert.Equal(10, clock.Start);
        Assert.Equal(20, clock.Stop);
        Assert.Equal(10, clock.Elapsed);
        Assert.Equal(9, clock.Exposure);
        Assert.Equal("JPL-DE440", clock.EphemerisReference);
    }

    [Fact]
    public void SupportsStandardReferencePositionDirectionAndObserverKeywords()
    {
        var clock = FitsClock.FromHeader(FitsHeader.Builder()
            .Text("TREFPOS", "CUSTOM")
            .Text("TREFDIR", "RA---TAN,DEC--TAN")
            .Real("OBSGEO-L", 12)
            .Real("OBSGEO-B", 34)
            .Real("OBSGEO-H", 56)
            .Real("TIMSYER", 1)
            .Real("TIMRDER", 2)
            .Build());

        Assert.Equal("CUSTOM", clock.ReferencePosition);
        Assert.Equal("RA---TAN,DEC--TAN", clock.ReferenceDirection);
        Assert.Equal((12d, 34d, 56d), clock.ObserverGeodeticPosition);
        Assert.Equal(2, clock.RelativeError);
        Assert.Equal("DE405", FitsClock.FromHeader(FitsHeader.Builder().Build()).EphemerisReference);
    }

    [Fact]
    public void SplitReferenceTakesPrecedenceWhenBothMjdFormsArePresent()
    {
        var clock = FitsClock.FromHeader(FitsHeader.Builder()
            .Real("MJDREF", 59000.25)
            .Integer("MJDREFI", 59001)
            .Real("MJDREFF", 0.5)
            .Build());

        Assert.Equal(59001.5, clock.ReferenceModifiedJulianDate, 12);
    }

    [Fact]
    public void MatchesNasaFitsStandardModifiedJulianDateReferenceVector()
    {
        // FITS Standard 4.0 Section 9.2.2: 2000-01-01T12:00:00 UTC = MJD 51544.5.
        Assert.Equal(51544.5, FitsClock.ModifiedJulianDate("2000-01-01T12:00:00Z"), 12);
    }

    [Fact]
    public void MatchesAstropyErfaUtcTaiReferenceVector()
    {
        // Astropy/ERFA UTC(TAI) reference vector at the 2017-01-01 leap boundary.
        double utc = FitsClock.ModifiedJulianDate("2017-01-01T00:00:00Z");
        double tai = FitsClock.ConvertModifiedJulianDate(utc, "UTC", "TAI");

        Assert.Equal(37d / 86400d, tai - utc, 10);
    }

    [Fact]
    public void MatchesCfitsioDateToModifiedJulianDateReferenceVector()
    {
        // CFITSIO ffdt2mjd-compatible calendar conversion vector.
        Assert.Equal(51544, FitsClock.ModifiedJulianDate("2000-01-01T00:00:00Z"), 12);
    }

    [Theory]
    [InlineData("ms", 1d / 86_400_000d)]
    [InlineData("us", 1d / 86_400_000_000d)]
    [InlineData("ns", 1d / 86_400_000_000_000d)]
    [InlineData("min", 1d / 1440d)]
    [InlineData("h", 1d / 24d)]
    [InlineData("d", 1d)]
    [InlineData("a", 365.25)]
    [InlineData("yr", 365.25)]
    [InlineData("cy", 36525)]
    public void SupportsSectionNineTimeUnits(string unit, double expectedDays)
    {
        var clock = FitsClock.FromHeader(FitsHeader.Builder().Text("TIMEUNIT", unit).Build());

        Assert.Equal(expectedDays, clock.UnitDays, 15);
    }

    [Fact]
    public void RejectsNegativeTimeErrorsAndNonfiniteMetadata()
    {
        Assert.Throws<FitsException>(() => FitsClock.FromHeader(
            FitsHeader.Builder().Real("TIMSYER", -1).Build()));
        Assert.Throws<FitsException>(() => FitsClock.FromHeader(
            FitsHeader.Builder().Real("TIERRELA", -1).Build()));
        Assert.Throws<FitsException>(() => FitsClock.FromHeader(
            FitsHeader.Builder().Real("TIERABSO", -1).Build()));
        Assert.Throws<FitsException>(() => FitsClock.FromHeader(
            FitsHeader.Builder().Real("TIMEOFFS", double.PositiveInfinity).Build()));
    }

    [Fact]
    public void VersionedProvidersInterpolateReferenceVectorsOffline()
    {
        var earth = new IersEarthOrientationProvider(
            "IERS-finals2000A.test-2026a",
            (59000d, 0.1d),
            (59002d, 0.3d));
        var ephemeris = new JplEphemerisProvider(
            "JPL-DE440.test-2026a",
            (59000d, 1d, 0.01d),
            (59002d, 3d, 0.03d));

        Assert.Equal("IERS-finals2000A.test-2026a", earth.Version);
        Assert.Equal("JPL-DE440.test-2026a", ephemeris.Version);
        Assert.True(earth.TryGetUt1MinusUtc(59001, out double dut1));
        Assert.Equal(0.2, dut1, 12);
        Assert.True(ephemeris.TryGetTdbMinusTt(59001, out double tdbMinusTt));
        Assert.Equal(2, tdbMinusTt, 12);
        Assert.True(ephemeris.TryGetTcbMinusTdb(59001, out double tcbMinusTdb));
        Assert.Equal(0.02, tcbMinusTdb, 12);
    }

    [Fact]
    public void VersionedProvidersRejectEpochsOutsideTheirDeclaredCoverage()
    {
        var earth = new IersEarthOrientationProvider("IERS-test", (59000, 0.1));
        var ephemeris = new JplEphemerisProvider("JPL-test", (59000, 1, 0.01));

        Assert.False(earth.TryGetUt1MinusUtc(58999, out _));
        Assert.False(ephemeris.TryGetTdbMinusTt(59001, out _));
        Assert.Throws<FitsException>(() => FitsClock.ConvertModifiedJulianDate(
            59001, "UTC", "UT1", new FitsClockOptions(earth, ephemeris)));
        Assert.Throws<FitsException>(() => FitsClock.ConvertModifiedJulianDate(
            59001, "TT", "TDB", new FitsClockOptions(earth, ephemeris)));
    }

    [Fact]
    public void BuiltInReferenceProvidersExposeStableOfflineVersions()
    {
        Assert.NotEmpty(IersEarthOrientationProvider.Default.Version);
        Assert.NotEmpty(JplEphemerisProvider.Default.Version);
        Assert.True(IersEarthOrientationProvider.Default.TryGetUt1MinusUtc(59000, out _));
        Assert.True(JplEphemerisProvider.Default.TryGetTdbMinusTt(59000, out _));
    }

    [Fact]
    public void LeapSecondTableIncludesTheMostRecentPublishedTransition()
    {
        double before = FitsClock.ModifiedJulianDate("2016-12-31T23:59:59Z");
        double leap = FitsClock.ModifiedJulianDate("2016-12-31T23:59:60Z");
        double after = FitsClock.ModifiedJulianDate("2017-01-01T00:00:00Z");

        Assert.Equal(1d / 86400d, leap - before, 10);
        Assert.Equal(after, leap, 12);
        Assert.Equal(37d / 86400d, FitsClock.ConvertModifiedJulianDate(after, "UTC", "TAI") - after, 10);
    }

    [Fact]
    public void TcbAndTdbConversionsUseBothProviderCorrections()
    {
        var provider = new JplEphemerisProvider(
            "JPL-test",
            (59000d, 1d, 2d),
            (59002d, 1d, 2d));
        var options = new FitsClockOptions(Ephemeris: provider);

        double tdb = FitsClock.ConvertModifiedJulianDate(59000, "TT", "TDB", options);
        double tcb = FitsClock.ConvertModifiedJulianDate(59000, "TT", "TCB", options);

        Assert.Equal(59000 + (1d / 86400d), tdb, 12);
        Assert.Equal(59000 + (3d / 86400d), tcb, 12);
        Assert.Equal(59000, FitsClock.ConvertModifiedJulianDate(tcb, "TCB", "TT", options), 12);
    }
}
