using System.Globalization;
using System.Collections.Frozen;

namespace CFITS;

/// <summary>
/// Time reference from FITS section 9.
/// Calendar dates, timezone offsets, and day counts use <see cref="DateTimeOffset"/>.
/// UTC/TAI uses the published leap-second table. UT1 uses only explicitly
/// supported observed DUT1 epochs; TDB and TCB require an external ephemeris.
/// TT, TAI, GPS, and TCG use the closed relations in section 9.2.1.
/// </summary>
public sealed class FitsClock
{
    private const double UnixModifiedJulianDate = 40587d;
    private const double TaiToTerrestrialSeconds = 32.184d;
    private const double GpsToTaiSeconds = 19d;
    private const double GeocentricRate = 6.969290134e-10;
    private const double DynamicalOriginJulianDate = 2443144.5003725;
    private static readonly (DateTime Date, int Offset)[] LeapSeconds =
    [
        (new(1972, 1, 1), 10), (new(1972, 7, 1), 11), (new(1973, 1, 1), 12),
        (new(1974, 1, 1), 13), (new(1975, 1, 1), 14), (new(1976, 1, 1), 15),
        (new(1977, 1, 1), 16), (new(1978, 1, 1), 17), (new(1979, 1, 1), 18),
        (new(1980, 1, 1), 19), (new(1981, 7, 1), 20), (new(1982, 7, 1), 21),
        (new(1983, 7, 1), 22), (new(1985, 7, 1), 23), (new(1988, 1, 1), 24),
        (new(1990, 1, 1), 25), (new(1991, 1, 1), 26), (new(1992, 7, 1), 27),
        (new(1993, 7, 1), 28), (new(1994, 7, 1), 29), (new(1996, 1, 1), 30),
        (new(1997, 7, 1), 31), (new(1999, 1, 1), 32), (new(2006, 1, 1), 33),
        (new(2009, 1, 1), 34), (new(2012, 7, 1), 35), (new(2015, 7, 1), 36),
        (new(2017, 1, 1), 37),
    ];
    private static readonly (DateTime Date, double Dut1)[] Ut1Offsets =
    [
        (new(2000, 1, 1), 0.355),
        (new(2016, 1, 1), -0.093),
        (new(2017, 1, 1), 0.591),
        (new(2020, 1, 1), -0.177),
    ];
    private static readonly FrozenDictionary<string, double> FixedUnitDays =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["S"] = 1d / 86400d,
            ["MIN"] = 1d / 1440d,
            ["H"] = 1d / 24d,
            ["D"] = 1,
            ["A"] = 365.25,
            ["YR"] = 365.25,
            ["CY"] = 36525,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private FitsClock(string scale, double referenceModifiedJulianDate, double offsetDays, double unitDays, double resolution, double pixelReference, bool applyReference, string referencePosition, (double X, double Y, double Z)? observerPosition, (double Longitude, double Latitude, double Height)? observerGeodeticPosition, string? observerOrbit, string? referenceDirection, double scaleError, double relativeError, double absoluteError, double? start, double? stop, double? elapsed, double? exposure, string? ephemerisReference, FitsClockOptions? options)
    {
        Scale = scale;
        ReferenceModifiedJulianDate = referenceModifiedJulianDate;
        OffsetDays = offsetDays;
        UnitDays = unitDays;
        Resolution = resolution;
        ScaleError = scaleError;
        RelativeError = relativeError;
        AbsoluteError = absoluteError;
        Start = start;
        Stop = stop;
        Elapsed = elapsed;
        Exposure = exposure;
        EphemerisReference = ephemerisReference;
        PixelReference = pixelReference;
        ReferencePosition = referencePosition;
        ObserverPosition = observerPosition;
        ObserverGeodeticPosition = observerGeodeticPosition;
        ObserverOrbit = observerOrbit;
        ReferenceDirection = referenceDirection;
        _applyReference = applyReference;
        _referenceModifiedJulianDate = referenceModifiedJulianDate;
        _offsetDays = offsetDays;
        _offsetInUnits = offsetDays / unitDays;
        _pixelOffset = (pixelReference - 0.5) * resolution;
        _options = options ?? new FitsClockOptions();
    }

    public string Scale { get; }

    public double ReferenceModifiedJulianDate { get; }

    public double OffsetDays { get; }

    public double UnitDays { get; }

    public double Resolution { get; }

    public double BinWidth => Resolution;

    public double ScaleError { get; }

    public double RelativeError { get; }

    public double AbsoluteError { get; }

    public double? Start { get; }

    public double? Stop { get; }

    public double? Elapsed { get; }

    public double? Exposure { get; }

    public string? EphemerisReference { get; }

    public double PixelReference { get; }

    public double BinReferencePixel => PixelReference;

    public string ReferencePosition { get; }

    public (double X, double Y, double Z)? ObserverPosition { get; }

    public (double Longitude, double Latitude, double Height)? ObserverGeodeticPosition { get; }

    public string? ObserverOrbit { get; }

    public string? ReferenceDirection { get; }

    public string? EarthOrientationVersion => _options.EarthOrientation?.Version;

    public string? EphemerisVersion => _options.Ephemeris?.Version;

    private readonly bool _applyReference;
    private readonly double _referenceModifiedJulianDate;
    private readonly double _offsetDays;
    private readonly double _offsetInUnits;
    private readonly double _pixelOffset;
    private readonly FitsClockOptions _options;

    public static FitsClock FromHeader(FitsHeader header) => FromHeader(header, null, null);

    public static FitsClock FromHeader(FitsHeader header, FitsClockOptions options) =>
        FromHeader(header, null, options);

    public static FitsClock FromHeader(FitsHeader header, int axis)
    {
        if (axis < 1)
        {
            throw new FitsException("A time axis number must be positive.", new FitsLocation());
        }

        return FromHeader(header, (int?)axis, null);
    }

    public static FitsClock FromHeader(FitsHeader header, int axis, FitsClockOptions options)
    {
        if (axis < 1)
        {
            throw new FitsException("A time axis number must be positive.", new FitsLocation());
        }

        return FromHeader(header, (int?)axis, options);
    }

    private static FitsClock FromHeader(FitsHeader header, int? axis, FitsClockOptions? options)
    {
        string scale = Text(header, "TIMESYS", axis) ?? "UTC";
        scale = Canonical(scale);
        ValidateScale(scale, options?.Ephemeris);
        double reference = Reference(header, axis);
        string unit = axis.HasValue
            ? Text(header, $"CUNIT{axis.Value}", null) ?? Text(header, "TIMEUNIT", axis) ?? "s"
            : Text(header, "TIMEUNIT", null) ?? "s";
        double unitDays = DaysPerUnit(unit.Trim(), reference);
        double offset = Offset(header, axis, unitDays);
        double resolution = Card(header, "TIMEDEL", axis, out var step)
            ? Finite(step.GetDouble(), "TIMEDEL")
            : 0;
        if (resolution < 0)
        {
            throw new FitsException("TIMEDEL must not be negative.", new FitsLocation(Keyword: "TIMEDEL"));
        }
        double pixel = Card(header, "TIMEPIXR", axis, out var phase)
            ? Finite(phase.GetDouble(), "TIMEPIXR")
            : 0.5;
        if (pixel is < 0 or > 1)
        {
            throw new FitsException("TIMEPIXR must be between zero and one.", new FitsLocation(Keyword: "TIMEPIXR"));
        }

        double scaleError = Error(header, "TIMSYER", axis);
        double relativeError = Error(header, "TIMRDER", axis, "TIERRELA");
        double absoluteError = Error(header, "TIERABSO", axis);
        return new FitsClock(
            scale,
            reference,
            offset,
            unitDays,
            resolution,
            pixel,
            scale != "LOCAL",
            GetReferencePosition(header, axis),
            GetObserverPosition(header, axis),
            GetObserverGeodeticPosition(header, axis),
            Text(header, "OBSORBIT", axis),
            Text(header, "TREFDIR", axis) ?? Text(header, "TRDIR", axis),
            scaleError,
            relativeError,
            absoluteError,
            Optional(header, "TSTART", axis),
            Optional(header, "TSTOP", axis),
            Optional(header, "TELAPSE", axis),
            Optional(header, "XPOSURE", axis),
            Text(header, "PLEPHEM", axis) ?? Text(header, "EPHEM", axis) ?? "DE405",
            options);
    }

    public double ToModifiedJulianDate(double time)
    {
        return _applyReference
            ? _referenceModifiedJulianDate + ((_offsetInUnits + time) * UnitDays)
            : time * UnitDays;
    }

    public double ToModifiedJulianDate(double time, string scale) =>
        ConvertModifiedJulianDate(ToModifiedJulianDate(time), Scale, scale, _options);

    public DateTimeOffset ToDateTimeOffset(double time)
    {
        double seconds = (ToModifiedJulianDate(time, "UTC") - UnixModifiedJulianDate) * 86400d;
        return DateTimeOffset.UnixEpoch.AddSeconds(seconds);
    }

    public double AtPixelReference(double centeredTime) => centeredTime + _pixelOffset;

    public static double ModifiedJulianDate(ReadOnlySpan<char> iso) => ModifiedJulianDate(Parse(iso));

    public static double ModifiedJulianDate(DateTimeOffset instant)
    {
        double seconds = (instant - DateTimeOffset.UnixEpoch).TotalSeconds;
        return UnixModifiedJulianDate + (seconds / 86400d);
    }

    public static DateTimeOffset Parse(ReadOnlySpan<char> iso)
    {
        string text = iso.Trim().ToString();
        int leapMarker = text.IndexOf(":60", StringComparison.Ordinal);
        if (leapMarker >= 0)
        {
            string normalized = text[..leapMarker] + ":59" + text[(leapMarker + 3)..];
            if (DateTimeOffset.TryParse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var before))
            {
                if (!text.EndsWith("Z", StringComparison.Ordinal) ||
                    !LeapSeconds.Any(entry => entry.Date == before.UtcDateTime.Date.AddDays(1) &&
                        before.UtcDateTime.TimeOfDay == new TimeSpan(23, 59, 59)))
                {
                    throw new FitsException($"'{text}' is not a published UTC leap second.", new FitsLocation(Keyword: "DATEREF"));
                }

                return before.AddSeconds(1);
            }
        }

        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset instant))
        {
            throw new FitsException($"'{text}' is not a date recognized by the runtime.", new FitsLocation());
        }

        return instant;
    }

    public static double ConvertModifiedJulianDate(double modifiedJulianDate, string fromScale, string toScale)
        => ConvertModifiedJulianDate(modifiedJulianDate, fromScale, toScale, null);

    public static double ConvertModifiedJulianDate(double modifiedJulianDate, string fromScale, string toScale, FitsClockOptions? options)
    {
        string from = Canonical(fromScale);
        string to = Canonical(toScale);
        if (from is "TCB" or "TDB" || to is "TCB" or "TDB")
        {
            return ConvertEphemeris(modifiedJulianDate, from, to, options?.Ephemeris);
        }

        if (from == to)
        {
            return modifiedJulianDate;
        }

        if (from is "UTC" or "UT1" || to is "UTC" or "UT1")
        {
            if (from == "UTC" && to == "TAI")
            {
                return modifiedJulianDate + LeapSecondsAt(modifiedJulianDate) / 86400d;
            }

            if (from == "TAI" && to == "UTC")
            {
                return modifiedJulianDate - LeapSecondsAt(modifiedJulianDate - 37d / 86400d) / 86400d;
            }

            if (from == "UTC" && to == "UT1")
            {
                return modifiedJulianDate + Ut1OffsetAt(modifiedJulianDate, options?.EarthOrientation) / 86400d;
            }

            if (from == "UT1" && to == "UTC")
            {
                return modifiedJulianDate - Ut1OffsetAt(modifiedJulianDate, options?.EarthOrientation) / 86400d;
            }

            if (from == "UTC")
            {
                double tai = modifiedJulianDate + LeapSecondsAt(modifiedJulianDate) / 86400d;
                return ConvertModifiedJulianDate(tai, "TAI", to);
            }

            if (from == "UT1")
            {
                double utc = modifiedJulianDate - Ut1OffsetAt(modifiedJulianDate, options?.EarthOrientation) / 86400d;
                return ConvertModifiedJulianDate(utc, "UTC", to, options);
            }

            if (to == "UTC")
            {
                double terrestrialDate = ToTerrestrial(modifiedJulianDate, from);
                double tai = terrestrialDate - TaiToTerrestrialSeconds / 86400d;
                return tai - LeapSecondsAt(tai - 37d / 86400d) / 86400d;
            }

            throw new FitsException($"'{fromScale}' and '{toScale}' require observed Earth-orientation data unavailable to the runtime.", new FitsLocation(Keyword: "TIMESYS"));
        }

        double terrestrial = ToTerrestrial(modifiedJulianDate, from);
        return FromTerrestrial(terrestrial, to);
    }

    public static double TropicalYearInDays(double julianCenturiesSinceJ2000)
    {
        double century = julianCenturiesSinceJ2000;
        return 365.24219040211236 - (0.00000615251349 * century) - (6.0921e-10 * century * century) + (2.6525e-10 * century * century * century);
    }

    public static double BesselianYearInDays(double julianCenturiesSinceJ1900) =>
        365.2421987817 - (0.00000785423 * julianCenturiesSinceJ1900);

    private static double ToTerrestrial(double modifiedJulianDate, string scale) => scale switch
    {
        "TT" or "ET" => modifiedJulianDate,
        "TAI" => modifiedJulianDate + (TaiToTerrestrialSeconds / 86400d),
        "GPS" => modifiedJulianDate + ((GpsToTaiSeconds + TaiToTerrestrialSeconds) / 86400d),
        "TCG" => GeocentricToTerrestrial(modifiedJulianDate),
        _ => throw new FitsException($"Time scale '{scale}' has no closed conversion in the FITS standard.", new FitsLocation(Keyword: "TIMESYS")),
    };

    private static double FromTerrestrial(double modifiedJulianDate, string scale) => scale switch
    {
        "TT" or "ET" => modifiedJulianDate,
        "TAI" => modifiedJulianDate - (TaiToTerrestrialSeconds / 86400d),
        "GPS" => modifiedJulianDate - ((GpsToTaiSeconds + TaiToTerrestrialSeconds) / 86400d),
        "TCG" => TerrestrialToGeocentric(modifiedJulianDate),
        _ => throw new FitsException($"Time scale '{scale}' has no closed conversion in the FITS standard.", new FitsLocation(Keyword: "TIMESYS")),
    };

    private static double TerrestrialToGeocentric(double terrestrial)
    {
        double julian = terrestrial + 2400000.5;
        return terrestrial + (GeocentricRate * (julian - DynamicalOriginJulianDate));
    }

    private static double GeocentricToTerrestrial(double geocentric)
    {
        double origin = DynamicalOriginJulianDate - 2400000.5;
        return origin + ((geocentric - origin) / (1d + GeocentricRate));
    }

    private static string Canonical(string scale)
    {
        string text = scale.Trim();
        int qualifier = text.IndexOf('(');
        if (qualifier > 0)
        {
            text = text[..qualifier];
        }

        return text.ToUpperInvariant() switch
        {
            "TDT" or "ET" => "TT",
            "IAT" => "TAI",
            "GMT" => "UTC",
            var other => other,
        };
    }

    private static int LeapSecondsAt(double modifiedJulianDate)
    {
        DateTime instant = DateTimeOffset.UnixEpoch.AddDays(modifiedJulianDate - UnixModifiedJulianDate).UtcDateTime;
        int low = 0;
        int high = LeapSeconds.Length - 1;
        int offset = 0;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            if (instant >= LeapSeconds[middle].Date)
            {
                offset = LeapSeconds[middle].Offset;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        if (offset == 0)
        {
            throw new FitsException("UTC/TAI conversion is unavailable before the first leap-second table entry.", new FitsLocation(Keyword: "TIMESYS"));
        }

        return offset;
    }

    private static double Ut1OffsetAt(double modifiedJulianDate, IEarthOrientationProvider? provider)
    {
        if (provider is not null)
        {
            if (!provider.TryGetUt1MinusUtc(modifiedJulianDate, out double supplied))
            {
                throw new FitsException(
                    $"Earth-orientation provider '{provider.Version}' has no data for MJD {modifiedJulianDate:R}.",
                    new FitsLocation(Keyword: "TIMESYS"));
            }

            return Finite(supplied, "UT1 provider result");
        }

        DateTime instant = DateTimeOffset.UnixEpoch.AddDays(modifiedJulianDate - UnixModifiedJulianDate).UtcDateTime;
        int low = 0;
        int high = Ut1Offsets.Length - 1;
        int selected = -1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            if (instant >= Ut1Offsets[middle].Date)
            {
                selected = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return selected >= 0
            ? Ut1Offsets[selected].Dut1
            : throw new FitsException("UT1 conversion requires an observed DUT1 value for the requested epoch.", new FitsLocation(Keyword: "TIMESYS"));
    }

    private static double ConvertEphemeris(double modifiedJulianDate, string from, string to, IEphemerisProvider? provider)
    {
        if (provider is null)
        {
            throw EphemerisRequired(from, to);
        }

        if (from == to)
        {
            return modifiedJulianDate;
        }

        double terrestrial = from switch
        {
            "TT" => modifiedJulianDate,
            "TDB" => TdbToTt(modifiedJulianDate, provider),
            "TCB" => TdbToTt(TcbToTdb(modifiedJulianDate, provider), provider),
            _ => ConvertModifiedJulianDate(modifiedJulianDate, from, "TT", new FitsClockOptions(Ephemeris: provider)),
        };
        double tdb = ToTdb(terrestrial, provider);
        return to switch
        {
            "TT" => terrestrial,
            "TDB" => tdb,
            "TCB" => ToTcb(tdb, provider),
            _ => ConvertModifiedJulianDate(terrestrial, "TT", to, new FitsClockOptions(Ephemeris: provider)),
        };
    }

    private static double ToTdb(double terrestrial, IEphemerisProvider provider) =>
        provider.TryGetTdbMinusTt(terrestrial, out double seconds)
            ? terrestrial + (Finite(seconds, "TDB-TT provider result") / 86400d)
            : throw EphemerisRequired("TT", "TDB");

    private static double TdbToTt(double barycentric, IEphemerisProvider provider)
    {
        double terrestrial = barycentric;
        for (int iteration = 0; iteration < 4; iteration++)
        {
            double next = barycentric - (ToTdb(terrestrial, provider) - terrestrial);
            if (Math.Abs(next - terrestrial) < 1e-15)
            {
                return next;
            }

            terrestrial = next;
        }

        return terrestrial;
    }

    private static double TcbToTdb(double barycentric, IEphemerisProvider provider)
    {
        if (!provider.TryGetTcbMinusTdb(barycentric, out double seconds))
        {
            throw EphemerisRequired("TCB", "TDB");
        }

        return barycentric - (Finite(seconds, "TCB-TDB provider result") / 86400d);
    }

    private static double ToTcb(double barycentric, IEphemerisProvider provider)
    {
        if (!provider.TryGetTcbMinusTdb(barycentric, out double seconds))
        {
            throw EphemerisRequired("TDB", "TCB");
        }

        return barycentric + (Finite(seconds, "TCB-TDB provider result") / 86400d);
    }

    private static double Reference(FitsHeader header, int? axis)
    {
        bool hasMjd = Card(header, "MJDREF", axis, out var modified);
        bool hasMjdInteger = Card(header, "MJDREFI", axis, out var modifiedInteger);
        bool hasMjdFraction = Card(header, "MJDREFF", axis, out var modifiedFraction);
        if (hasMjd && hasMjdInteger && hasMjdFraction)
        {
            return Finite(modifiedInteger.GetDouble(), "MJDREFI")
                + Finite(modifiedFraction.GetDouble(), "MJDREFF");
        }

        if (hasMjd)
        {
            return Finite(modified.GetDouble(), "MJDREF");
        }

        if (hasMjdInteger)
        {
            double integer = Finite(modifiedInteger.GetDouble(), "MJDREFI");
            double fraction = hasMjdFraction ? Finite(modifiedFraction.GetDouble(), "MJDREFF") : 0;
            return integer + fraction;
        }

        bool hasJd = Card(header, "JDREF", axis, out var julian);
        bool hasJdInteger = Card(header, "JDREFI", axis, out var julianInteger);
        bool hasJdFraction = Card(header, "JDREFF", axis, out var julianFraction);
        if (hasJd && hasJdInteger && hasJdFraction)
        {
            return Finite(julianInteger.GetDouble(), "JDREFI")
                + Finite(julianFraction.GetDouble(), "JDREFF")
                - 2400000.5;
        }

        if (hasJd)
        {
            return Finite(julian.GetDouble(), "JDREF") - 2400000.5;
        }

        if (hasJdInteger)
        {
            double integer = Finite(julianInteger.GetDouble(), "JDREFI");
            double fraction = hasJdFraction ? Finite(julianFraction.GetDouble(), "JDREFF") : 0;
            return integer + fraction - 2400000.5;
        }

        if (Card(header, "DATEREF", axis, out var date) && date.GetString() is string text)
        {
            return ModifiedJulianDate(text);
        }

        return 0;
    }

    private static string GetReferencePosition(FitsHeader header, int? axis)
    {
        if (!Card(header, "TREFPOS", axis, out var reference)
            && !Card(header, "TRPOS", axis, out reference)
            && !Card(header, "TIMEREF", axis, out reference))
        {
            return "TOPOCENTER";
        }

        string value = reference.GetString()?.Trim().ToUpperInvariant() ?? "";
        string normalized = value.Length >= 3 ? value[..3] : value;
        string canonical = normalized switch
        {
            "TOP" => "TOPOCENTER",
            "GEO" => "GEOCENTER",
            "BAR" => "BARYCENTER",
            "REL" => "RELOCATABLE",
            "CUS" => "CUSTOM",
            "HEL" => "HELIOCENTER",
            "GAL" => "GALACTIC",
            "EMB" => "EMBARYCENTER",
            "MER" or "VEN" or "MAR" or "JUP" or "SAT" or "URA" or "NEP" => value,
            _ => "",
        };
        if (canonical.Length == 0)
        {
            throw new FitsException($"TREFPOS '{value}' is not a supported FITS reference position.", new FitsLocation(Keyword: reference.Keyword));
        }

        return canonical;
    }

    private static (double X, double Y, double Z)? GetObserverPosition(FitsHeader header, int? axis)
    {
        bool hasX = Card(header, "OBSGEO-X", axis, out var xCard);
        bool hasY = Card(header, "OBSGEO-Y", axis, out var yCard);
        bool hasZ = Card(header, "OBSGEO-Z", axis, out var zCard);
        if (!hasX && !hasY && !hasZ)
        {
            return null;
        }

        if (!hasX || !hasY || !hasZ)
        {
            throw new FitsException("OBSGEO-X, OBSGEO-Y, and OBSGEO-Z must be supplied together.", new FitsLocation(Keyword: "OBSGEO-X"));
        }

        return (
            Finite(xCard.GetDouble(), "OBSGEO-X"),
            Finite(yCard.GetDouble(), "OBSGEO-Y"),
            Finite(zCard.GetDouble(), "OBSGEO-Z"));
    }

    private static (double Longitude, double Latitude, double Height)? GetObserverGeodeticPosition(FitsHeader header, int? axis)
    {
        bool hasLongitude = Card(header, "OBSGEO-L", axis, out var longitude);
        bool hasLatitude = Card(header, "OBSGEO-B", axis, out var latitude);
        bool hasHeight = Card(header, "OBSGEO-H", axis, out var height);
        if (!hasLongitude && !hasLatitude && !hasHeight)
        {
            return null;
        }

        if (!hasLongitude || !hasLatitude || !hasHeight)
        {
            throw new FitsException("OBSGEO-L, OBSGEO-B, and OBSGEO-H must be supplied together.", new FitsLocation(Keyword: "OBSGEO-L"));
        }

        return (
            Finite(longitude.GetDouble(), "OBSGEO-L"),
            Finite(latitude.GetDouble(), "OBSGEO-B"),
            Finite(height.GetDouble(), "OBSGEO-H"));
    }

    private static void ValidateScale(string scale, IEphemerisProvider? ephemeris)
    {
        if (scale is "TDB" or "TCB" && ephemeris is null)
        {
            throw EphemerisRequired(scale, scale);
        }

        if (scale is not ("UTC" or "UT1" or "TAI" or "TT" or "ET" or "TCG" or "GPS" or "LOCAL" or "TDB" or "TCB"))
        {
            throw new FitsException($"Time scale '{scale}' is not supported.", new FitsLocation(Keyword: "TIMESYS"));
        }
    }

    private static FitsException EphemerisRequired(string fromScale, string toScale) =>
        new(
            $"'{fromScale}' and '{toScale}' require an external ephemeris (PLEPHEM/EPHEM); no ephemeris provider is configured.",
            new FitsLocation(Keyword: "TIMESYS"));

    private static double Finite(double value, string keyword)
    {
        if (!double.IsFinite(value))
        {
            throw new FitsException($"{keyword} must be finite.", new FitsLocation(Keyword: keyword));
        }

        return value;
    }

    private static double DaysPerUnit(string unit, double referenceModifiedJulianDate)
    {
        if (FixedUnitDays.TryGetValue(unit, out double days))
        {
            return days;
        }

        return unit.ToUpperInvariant() switch
        {
            "MS" => 1d / 86_400_000d,
            "US" => 1d / 86_400_000_000d,
            "NS" => 1d / 86_400_000_000_000d,
            "DAY" or "DAYS" => 1,
            "TA" => TropicalYearInDays((referenceModifiedJulianDate - 51544.5) / 36525d),
            "BA" => BesselianYearInDays((referenceModifiedJulianDate - 15019.5) / 36525d),
            _ => throw new FitsException($"TIMEUNIT '{unit}' is not a recognized time unit.", new FitsLocation(Keyword: "TIMEUNIT")),
        };
    }

    private static double Offset(FitsHeader header, int? axis, double unitDays)
    {
        if (Card(header, "TIMEOFFS", axis, out var offset))
        {
            return Finite(offset.GetDouble(), "TIMEOFFS") * unitDays;
        }

        return Card(header, "TIMEZERO", axis, out var legacy)
            ? Finite(legacy.GetDouble(), "TIMEZERO") * unitDays
            : 0;
    }

    private static double Error(FitsHeader header, string keyword, int? axis)
        => Error(header, keyword, axis, null);

    private static double Error(FitsHeader header, string keyword, int? axis, string? legacyKeyword)
    {
        if (!Card(header, keyword, axis, out var card)
            && (legacyKeyword is null || !Card(header, legacyKeyword, axis, out card)))
        {
            return 0;
        }

        double value = Finite(card.GetDouble(), keyword);
        if (value < 0)
        {
            throw new FitsException($"{keyword} must not be negative.", new FitsLocation(Keyword: keyword));
        }

        return value;
    }

    private static double? Optional(FitsHeader header, string keyword, int? axis)
    {
        return Card(header, keyword, axis, out var card)
            ? Finite(card.GetDouble(), keyword)
            : null;
    }

    private static string? Text(FitsHeader header, string keyword, int? axis)
    {
        if (axis.HasValue && header.TryGetCard(keyword + axis.Value, out var axisCard))
        {
            return axisCard.GetString();
        }

        return header.TryGetCard(keyword, out var card) ? card.GetString() : null;
    }

    private static bool Card(FitsHeader header, string keyword, int? axis, out FitsCard card)
    {
        if (axis.HasValue && header.TryGetCard(keyword + axis.Value, out card))
        {
            return true;
        }

        return header.TryGetCard(keyword, out card);
    }
}
