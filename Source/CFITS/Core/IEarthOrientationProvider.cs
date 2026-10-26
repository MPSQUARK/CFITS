namespace CFITS;

/// <summary>
/// Supplies versioned observed Earth-orientation data for UT1 conversions.
/// Implementations should identify the exact IERS bulletin or data release in <see cref="Version"/>.
/// </summary>
public interface IEarthOrientationProvider
{
    string Version { get; }

    bool TryGetUt1MinusUtc(double utcModifiedJulianDate, out double seconds);
}
