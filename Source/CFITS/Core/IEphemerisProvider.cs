namespace CFITS;

/// <summary>
/// Supplies versioned relativistic ephemeris corrections for TDB and TCB.
/// </summary>
public interface IEphemerisProvider
{
    string Version { get; }

    bool TryGetTdbMinusTt(double terrestrialTimeModifiedJulianDate, out double seconds);

    bool TryGetTcbMinusTdb(double barycentricDynamicalTimeModifiedJulianDate, out double seconds);
}
