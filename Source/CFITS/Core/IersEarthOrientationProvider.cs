namespace CFITS;

/// <summary>
/// Offline, deterministic UT1 provider backed by versioned IERS-compatible
/// samples. Samples are linearly interpolated within their declared coverage.
/// </summary>
public sealed class IersEarthOrientationProvider : IEarthOrientationProvider
{
    private readonly double[] _dates;
    private readonly double[] _ut1MinusUtc;

    public IersEarthOrientationProvider(
        string version,
        params (double ModifiedJulianDate, double Ut1MinusUtcSeconds)[] samples)
    {
        Version = RequireVersion(version);
        (_dates, _ut1MinusUtc) = DatedSampleTables.Split(samples);
    }

    public static IersEarthOrientationProvider Default { get; } = new(
        "IERS-finals2000A.compact-2026a",
        (51544.5, 0.355),
        (57754.0, -0.093),
        (57754.5, 0.591),
        (58849.0, -0.177),
        (59000.0, -0.177));

    public string Version { get; }

    public bool TryGetUt1MinusUtc(double utcModifiedJulianDate, out double seconds) =>
        DatedSamples.TryInterpolate(_dates, _ut1MinusUtc, utcModifiedJulianDate, out seconds);

    private static string RequireVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("A provider version is required.", nameof(version));
        }

        return version;
    }
}
