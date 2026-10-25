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

    private FitsClock(string scale, double referenceModifiedJulianDate, double offsetDays, double unitDays, double resolution, double pixelReference, bool applyReference, string referencePosition, (double X, double Y, double Z)? observerPosition)
    {
        Scale = scale;
        ReferenceModifiedJulianDate = referenceModifiedJulianDate;
        OffsetDays = offsetDays;
        UnitDays = unitDays;
        Resolution = resolution;
        PixelReference = pixelReference;
        ReferencePosition = referencePosition;
        ObserverPosition = observerPosition;
        _applyReference = applyReference;
        _referenceModifiedJulianDate = referenceModifiedJulianDate;
        _offsetDays = offsetDays;
        _pixelOffset = (pixelReference - 0.5) * resolution;
    }

    public string Scale { get; }

    public double ReferenceModifiedJulianDate { get; }

    public double OffsetDays { get; }

    public double UnitDays { get; }

    public double Resolution { get; }

    public double PixelReference { get; }

    public string ReferencePosition { get; }

    public (double X, double Y, double Z)? ObserverPosition { get; }

    private readonly bool _applyReference;
    private readonly double _referenceModifiedJulianDate;
    private readonly double _offsetDays;
    private readonly double _pixelOffset;

    public static FitsClock FromHeader(FitsHeader header)
    {
        string scale = header.TryGetCard("TIMESYS", out var timeScale) ? timeScale.GetString() ?? "UTC" : "UTC";
        scale = Canonical(scale);
        ValidateScale(scale);
        double reference = Reference(header);
        string unit = header.TryGetCard("TIMEUNIT", out var timeUnit) ? timeUnit.GetString() ?? "s" : "s";
        double unitDays = DaysPerUnit(unit.Trim(), reference);
        double offset = header.TryGetCard("TIMEOFFS", out var correction)
            ? Finite(correction.GetDouble(), "TIMEOFFS") * unitDays
            : 0;
        double resolution = header.TryGetCard("TIMEDEL", out var step)
            ? Finite(step.GetDouble(), "TIMEDEL")
            : 0;
        if (resolution < 0)
        {
            throw new FitsException("TIMEDEL must not be negative.", new FitsLocation(Keyword: "TIMEDEL"));
        }
        double pixel = header.TryGetCard("TIMEPIXR", out var phase)
            ? Finite(phase.GetDouble(), "TIMEPIXR")
            : 0.5;
        if (pixel is < 0 or > 1)
        {
            throw new FitsException("TIMEPIXR must be between zero and one.", new FitsLocation(Keyword: "TIMEPIXR"));
        }

        return new FitsClock(scale, reference, offset, unitDays, resolution, pixel, scale != "LOCAL", GetReferencePosition(header), GetObserverPosition(header));
    }

    public double ToModifiedJulianDate(double time)
    {
        double elapsed = time * UnitDays;
        return _applyReference
            ? _referenceModifiedJulianDate + ((_offsetDays / UnitDays + time) * UnitDays)
            : elapsed;
    }

    public double ToModifiedJulianDate(double time, string scale) =>
        ConvertModifiedJulianDate(ToModifiedJulianDate(time), Scale, scale);

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
    {
        string from = Canonical(fromScale);
        string to = Canonical(toScale);
        if (from is "TCB" or "TDB" || to is "TCB" or "TDB")
        {
            throw EphemerisRequired(fromScale, toScale);
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
                return modifiedJulianDate + Ut1OffsetAt(modifiedJulianDate) / 86400d;
            }

            if (from == "UT1" && to == "UTC")
            {
                return modifiedJulianDate - Ut1OffsetAt(modifiedJulianDate) / 86400d;
            }

            if (from == "UTC")
            {
                double tai = modifiedJulianDate + LeapSecondsAt(modifiedJulianDate) / 86400d;
                return ConvertModifiedJulianDate(tai, "TAI", to);
            }

            if (from == "UT1")
            {
                double utc = modifiedJulianDate - Ut1OffsetAt(modifiedJulianDate) / 86400d;
                return ConvertModifiedJulianDate(utc, "UTC", to);
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
        int offset = 0;
        foreach (var entry in LeapSeconds)
        {
            if (instant >= entry.Date)
            {
                offset = entry.Offset;
            }
        }

        if (offset == 0)
        {
            throw new FitsException("UTC/TAI conversion is unavailable before the first leap-second table entry.", new FitsLocation(Keyword: "TIMESYS"));
        }

        return offset;
    }

    private static double Ut1OffsetAt(double modifiedJulianDate)
    {
        DateTime instant = DateTimeOffset.UnixEpoch.AddDays(modifiedJulianDate - UnixModifiedJulianDate).UtcDateTime;
        for (int index = Ut1Offsets.Length - 1; index >= 0; index--)
        {
            if (instant >= Ut1Offsets[index].Date)
            {
                return Ut1Offsets[index].Dut1;
            }
        }

        throw new FitsException("UT1 conversion requires an observed DUT1 value for the requested epoch.", new FitsLocation(Keyword: "TIMESYS"));
    }

    private static double Reference(FitsHeader header)
    {
        if (header.TryGetCard("MJDREF", out var modified))
        {
            return Finite(modified.GetDouble(), "MJDREF");
        }

        if (header.TryGetCard("MJDREFI", out modified))
        {
            double integer = Finite(modified.GetDouble(), "MJDREFI");
            double fraction = header.TryGetCard("MJDREFF", out var fractional) ? Finite(fractional.GetDouble(), "MJDREFF") : 0;
            return integer + fraction;
        }

        if (header.TryGetCard("JDREF", out var julian))
        {
            return Finite(julian.GetDouble(), "JDREF") - 2400000.5;
        }

        if (header.TryGetCard("JDREFI", out julian))
        {
            double integer = Finite(julian.GetDouble(), "JDREFI");
            double fraction = header.TryGetCard("JDREFF", out var fractional) ? Finite(fractional.GetDouble(), "JDREFF") : 0;
            return integer + fraction - 2400000.5;
        }

        if (header.TryGetCard("DATEREF", out var date) && date.GetString() is string text)
        {
            return ModifiedJulianDate(text);
        }

        return 0;
    }

    private static string GetReferencePosition(FitsHeader header)
    {
        if (!header.TryGetCard("TIMEREF", out var reference))
        {
            return "GEOCENTER";
        }

        string value = reference.GetString()?.Trim().ToUpperInvariant() ?? "";
        if (value is not ("TOPOCENTER" or "GEOCENTER" or "BARYCENTER"))
        {
            throw new FitsException($"TIMEREF '{value}' is not a supported FITS reference position.", new FitsLocation(Keyword: "TIMEREF"));
        }

        return value;
    }

    private static (double X, double Y, double Z)? GetObserverPosition(FitsHeader header)
    {
        bool hasX = header.TryGetCard("OBSGEO-X", out var xCard);
        bool hasY = header.TryGetCard("OBSGEO-Y", out var yCard);
        bool hasZ = header.TryGetCard("OBSGEO-Z", out var zCard);
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

    private static void ValidateScale(string scale)
    {
        if (scale is "TDB" or "TCB")
        {
            throw EphemerisRequired(scale, scale);
        }

        if (scale is not ("UTC" or "UT1" or "TAI" or "TT" or "ET" or "TCG" or "GPS" or "LOCAL"))
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
            "TA" => TropicalYearInDays((referenceModifiedJulianDate - 51544.5) / 36525d),
            "BA" => BesselianYearInDays((referenceModifiedJulianDate - 15019.5) / 36525d),
            _ => throw new FitsException($"TIMEUNIT '{unit}' is not a recognized time unit.", new FitsLocation(Keyword: "TIMEUNIT")),
        };
    }
}
