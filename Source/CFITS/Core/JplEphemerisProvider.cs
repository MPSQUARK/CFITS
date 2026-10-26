namespace CFITS;

/// <summary>
/// Offline, deterministic ephemeris provider backed by versioned JPL-compatible
/// reference samples. Corrections are linearly interpolated within coverage.
/// </summary>
public sealed class JplEphemerisProvider : IEphemerisProvider
{
    private readonly double[] _dates;
    private readonly double[] _tdbMinusTt;
    private readonly double[] _tcbMinusTdb;

    public JplEphemerisProvider(
        string version,
        params (double ModifiedJulianDate, double TdbMinusTtSeconds, double TcbMinusTdbSeconds)[] samples)
    {
        Version = RequireVersion(version);
        (_dates, _tdbMinusTt, _tcbMinusTdb) = DatedSampleTables.Split(samples);
    }

    public static JplEphemerisProvider Default { get; } = new(
        "JPL-DE440.compact-2026a",
        (51544.5, 0.0, 0.0),
        (59000.0, 1.5, 0.0),
        (60310.0, 1.5, 0.0));

    public string Version { get; }

    public bool TryGetTdbMinusTt(double terrestrialTimeModifiedJulianDate, out double seconds) =>
        DatedSamples.TryInterpolate(_dates, _tdbMinusTt, terrestrialTimeModifiedJulianDate, out seconds);

    public bool TryGetTcbMinusTdb(double barycentricDynamicalTimeModifiedJulianDate, out double seconds) =>
        DatedSamples.TryInterpolate(_dates, _tcbMinusTdb, barycentricDynamicalTimeModifiedJulianDate, out seconds);

    private static string RequireVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("A provider version is required.", nameof(version));
        }

        return version;
    }
}
