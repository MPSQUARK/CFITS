namespace CFITS;

/// <summary>
/// External, versioned data sources used by <see cref="FitsClock"/>.
/// </summary>
public sealed record FitsClockOptions(
    IEarthOrientationProvider? EarthOrientation = null,
    IEphemerisProvider? Ephemeris = null);
