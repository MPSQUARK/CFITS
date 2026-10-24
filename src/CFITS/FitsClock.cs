using System.Globalization;

namespace CFITS;

/// <summary>
/// Time reference from FITS section 9.
/// Calendar dates, timezone offsets, and day counts use <see cref="DateTimeOffset"/>.
/// The runtime does not represent leap seconds or Earth rotation, so UTC, UT1, and TDB ephemerides are not invented here.
/// TT, TAI, GPS, and TCG use the closed relations in section 9.2.1.
/// </summary>
public sealed class FitsClock
{
    private const double UnixModifiedJulianDate = 40587d;
    private const double TaiToTerrestrialSeconds = 32.184d;
    private const double GpsToTaiSeconds = 19d;
    private const double GeocentricRate = 6.969290134e-10;
    private const double BarycentricRate = 1.550519768e-8;
    private const double DynamicalOriginJulianDate = 2443144.5003725;
    private const double DynamicalOffsetSeconds = -6.55e-5;

    private FitsClock(string scale, double referenceModifiedJulianDate, double offsetDays, double unitDays, double resolution, double pixelReference, bool applyReference)
    {
        Scale = scale;
        ReferenceModifiedJulianDate = referenceModifiedJulianDate;
        OffsetDays = offsetDays;
        UnitDays = unitDays;
        Resolution = resolution;
        PixelReference = pixelReference;
        _applyReference = applyReference;
    }

    public string Scale { get; }

    public double ReferenceModifiedJulianDate { get; }

    public double OffsetDays { get; }

    public double UnitDays { get; }

    public double Resolution { get; }

    public double PixelReference { get; }

    private readonly bool _applyReference;

    public static FitsClock FromHeader(FitsHeader header)
    {
        string scale = header.TryGetCard("TIMESYS", out var timeScale) ? timeScale.GetString() ?? "UTC" : "UTC";
        scale = Canonical(scale);
        double reference = Reference(header);
        double offset = header.TryGetCard("TIMEOFFS", out var correction) ? correction.GetDouble() : 0;
        string unit = header.TryGetCard("TIMEUNIT", out var timeUnit) ? timeUnit.GetString() ?? "s" : "s";
        double resolution = header.TryGetCard("TIMEDEL", out var step) ? step.GetDouble() : 0;
        double pixel = header.TryGetCard("TIMEPIXR", out var phase) ? phase.GetDouble() : 0.5;
        return new FitsClock(scale, reference, offset, DaysPerUnit(unit.Trim(), reference), resolution, pixel, scale != "LOCAL");
    }

    public double ToModifiedJulianDate(double time)
    {
        double elapsed = time * UnitDays;
        return _applyReference ? ReferenceModifiedJulianDate + OffsetDays + elapsed : elapsed;
    }

    public double ToModifiedJulianDate(double time, string scale) =>
        ConvertModifiedJulianDate(ToModifiedJulianDate(time), Scale, scale);

    public DateTimeOffset ToDateTimeOffset(double time)
    {
        double seconds = (ToModifiedJulianDate(time, "UTC") - UnixModifiedJulianDate) * 86400d;
        return DateTimeOffset.UnixEpoch.AddSeconds(seconds);
    }

    public double AtPixelReference(double centeredTime) => centeredTime + ((PixelReference - 0.5) * Resolution);

    public static double ModifiedJulianDate(ReadOnlySpan<char> iso) => ModifiedJulianDate(Parse(iso));

    public static double ModifiedJulianDate(DateTimeOffset instant)
    {
        double seconds = (instant - DateTimeOffset.UnixEpoch).TotalSeconds;
        return UnixModifiedJulianDate + (seconds / 86400d);
    }

    public static DateTimeOffset Parse(ReadOnlySpan<char> iso)
    {
        string text = iso.Trim().ToString();
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
        if (from == to)
        {
            return modifiedJulianDate;
        }

        if (from is "TCB" or "TDB" || to is "TCB" or "TDB")
        {
            if (from is "TCB" or "TDB" && to is "TCB" or "TDB")
            {
                return from == "TCB" ? TerrestrialBarycentricToDynamical(modifiedJulianDate) : DynamicalToTerrestrialBarycentric(modifiedJulianDate);
            }

            throw new FitsException($"'{fromScale}' and '{toScale}' need the Irwin and Fukushima time ephemeris, which the FITS standard references and the runtime does not provide.", new FitsLocation(Keyword: "TIMESYS"));
        }

        if (from is "UTC" or "UT1" || to is "UTC" or "UT1")
        {
            throw new FitsException($"'{fromScale}' and '{toScale}' differ by leap seconds or Earth rotation. DateTimeOffset does not represent those adjustments.", new FitsLocation(Keyword: "TIMESYS"));
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

    private static double TerrestrialBarycentricToDynamical(double barycentric)
    {
        double julian = barycentric + 2400000.5;
        double seconds = -BarycentricRate * 86400d * (julian - DynamicalOriginJulianDate) + DynamicalOffsetSeconds;
        return barycentric + (seconds / 86400d);
    }

    private static double DynamicalToTerrestrialBarycentric(double dynamical)
    {
        double origin = DynamicalOriginJulianDate - 2400000.5;
        double offset = DynamicalOffsetSeconds / 86400d;
        return origin + ((dynamical - origin - offset) / (1d - BarycentricRate));
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

    private static double Reference(FitsHeader header)
    {
        if (header.TryGetCard("MJDREF", out var modified) || header.TryGetCard("MJDREFI", out modified))
        {
            double integer = modified.GetDouble();
            if (header.TryGetCard("MJDREF", out _))
            {
                return integer;
            }

            double fraction = header.TryGetCard("MJDREFF", out var fractional) ? fractional.GetDouble() : 0;
            return integer + fraction;
        }

        if (header.TryGetCard("JDREF", out var julian) || header.TryGetCard("JDREFI", out julian))
        {
            double integer = julian.GetDouble();
            double combined = header.TryGetCard("JDREF", out _)
                ? integer
                : integer + (header.TryGetCard("JDREFF", out var fractional) ? fractional.GetDouble() : 0);
            return combined - 2400000.5;
        }

        if (header.TryGetCard("DATEREF", out var date) && date.GetString() is string text)
        {
            return ModifiedJulianDate(text);
        }

        return 0;
    }

    private static double DaysPerUnit(string unit, double referenceModifiedJulianDate) => unit.ToUpperInvariant() switch
    {
        "S" => 1d / 86400d,
        "MIN" => 1d / 1440d,
        "H" => 1d / 24d,
        "D" => 1,
        "A" or "YR" => 365.25,
        "CY" => 36525,
        "TA" => TropicalYearInDays((referenceModifiedJulianDate - 51544.5) / 36525d),
        "BA" => BesselianYearInDays((referenceModifiedJulianDate - 15019.5) / 36525d),
        _ => throw new FitsException($"TIMEUNIT '{unit}' is not a recognized time unit.", new FitsLocation(Keyword: "TIMEUNIT")),
    };
}
