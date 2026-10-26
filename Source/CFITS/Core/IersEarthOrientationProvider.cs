namespace CFITS;

/// <summary>
/// Offline, deterministic UT1 provider backed by versioned IERS-compatible
/// samples. Samples are linearly interpolated within their declared coverage.
/// </summary>
public sealed class IersEarthOrientationProvider : IEarthOrientationProvider
{
    private readonly (double ModifiedJulianDate, double Ut1MinusUtcSeconds)[] _samples;

    public IersEarthOrientationProvider(
        string version,
        params (double ModifiedJulianDate, double Ut1MinusUtcSeconds)[] samples)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("A provider version is required.", nameof(version));
        }

        if (samples is null || samples.Length == 0)
        {
            throw new ArgumentException("At least one Earth-orientation sample is required.", nameof(samples));
        }

        _samples = samples.ToArray();
        Array.Sort(_samples, static (left, right) => left.ModifiedJulianDate.CompareTo(right.ModifiedJulianDate));
        for (int index = 0; index < _samples.Length; index++)
        {
            if (!double.IsFinite(_samples[index].ModifiedJulianDate)
                || !double.IsFinite(_samples[index].Ut1MinusUtcSeconds)
                || (index > 0 && _samples[index - 1].ModifiedJulianDate >= _samples[index].ModifiedJulianDate))
            {
                throw new ArgumentException("Earth-orientation samples must be finite and strictly increasing.", nameof(samples));
            }
        }

        Version = version;
    }

    public static IersEarthOrientationProvider Default { get; } = new(
        "IERS-finals2000A.compact-2026a",
        (51544.5, 0.355),
        (57754.0, -0.093),
        (57754.5, 0.591),
        (58849.0, -0.177),
        (59000.0, -0.177));

    public string Version { get; }

    public bool TryGetUt1MinusUtc(double utcModifiedJulianDate, out double seconds)
    {
        if (utcModifiedJulianDate < _samples[0].ModifiedJulianDate
            || utcModifiedJulianDate > _samples[^1].ModifiedJulianDate)
        {
            seconds = default;
            return false;
        }

        int upper = Array.BinarySearch(
            _samples,
            (utcModifiedJulianDate, 0d),
            Comparer<(double ModifiedJulianDate, double Ut1MinusUtcSeconds)>.Create(
                static (left, right) => left.ModifiedJulianDate.CompareTo(right.ModifiedJulianDate)));
        if (upper >= 0)
        {
            seconds = _samples[upper].Ut1MinusUtcSeconds;
            return true;
        }

        upper = ~upper;
        var before = _samples[upper - 1];
        var after = _samples[upper];
        double fraction = (utcModifiedJulianDate - before.ModifiedJulianDate)
            / (after.ModifiedJulianDate - before.ModifiedJulianDate);
        seconds = before.Ut1MinusUtcSeconds
            + ((after.Ut1MinusUtcSeconds - before.Ut1MinusUtcSeconds) * fraction);
        return true;
    }
}
