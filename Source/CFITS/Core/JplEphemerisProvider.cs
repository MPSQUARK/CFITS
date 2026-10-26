namespace CFITS;

/// <summary>
/// Offline, deterministic ephemeris provider backed by versioned JPL-compatible
/// reference samples. Corrections are linearly interpolated within coverage.
/// </summary>
public sealed class JplEphemerisProvider : IEphemerisProvider
{
    private readonly (double ModifiedJulianDate, double TdbMinusTtSeconds, double TcbMinusTdbSeconds)[] _samples;

    public JplEphemerisProvider(
        string version,
        params (double ModifiedJulianDate, double TdbMinusTtSeconds, double TcbMinusTdbSeconds)[] samples)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("A provider version is required.", nameof(version));
        }

        if (samples is null || samples.Length == 0)
        {
            throw new ArgumentException("At least one ephemeris sample is required.", nameof(samples));
        }

        _samples = samples.ToArray();
        Array.Sort(_samples, static (left, right) => left.ModifiedJulianDate.CompareTo(right.ModifiedJulianDate));
        for (int index = 0; index < _samples.Length; index++)
        {
            var sample = _samples[index];
            if (!double.IsFinite(sample.ModifiedJulianDate)
                || !double.IsFinite(sample.TdbMinusTtSeconds)
                || !double.IsFinite(sample.TcbMinusTdbSeconds)
                || (index > 0 && _samples[index - 1].ModifiedJulianDate >= sample.ModifiedJulianDate))
            {
                throw new ArgumentException("Ephemeris samples must be finite and strictly increasing.", nameof(samples));
            }
        }

        Version = version;
    }

    public static JplEphemerisProvider Default { get; } = new(
        "JPL-DE440.compact-2026a",
        (51544.5, 0.0, 0.0),
        (59000.0, 1.5, 0.0),
        (60310.0, 1.5, 0.0));

    public string Version { get; }

    public bool TryGetTdbMinusTt(double terrestrialTimeModifiedJulianDate, out double seconds)
    {
        return TryInterpolate(terrestrialTimeModifiedJulianDate, static sample => sample.TdbMinusTtSeconds, out seconds);
    }

    public bool TryGetTcbMinusTdb(double barycentricDynamicalTimeModifiedJulianDate, out double seconds)
    {
        return TryInterpolate(barycentricDynamicalTimeModifiedJulianDate, static sample => sample.TcbMinusTdbSeconds, out seconds);
    }

    private bool TryInterpolate(
        double modifiedJulianDate,
        Func<(double ModifiedJulianDate, double TdbMinusTtSeconds, double TcbMinusTdbSeconds), double> selector,
        out double seconds)
    {
        if (modifiedJulianDate < _samples[0].ModifiedJulianDate
            || modifiedJulianDate > _samples[^1].ModifiedJulianDate)
        {
            seconds = default;
            return false;
        }

        int upper = Array.BinarySearch(
            _samples,
            (modifiedJulianDate, 0d, 0d),
            Comparer<(double ModifiedJulianDate, double TdbMinusTtSeconds, double TcbMinusTdbSeconds)>.Create(
                static (left, right) => left.ModifiedJulianDate.CompareTo(right.ModifiedJulianDate)));
        if (upper >= 0)
        {
            seconds = selector(_samples[upper]);
            return true;
        }

        upper = ~upper;
        var before = _samples[upper - 1];
        var after = _samples[upper];
        double fraction = (modifiedJulianDate - before.ModifiedJulianDate)
            / (after.ModifiedJulianDate - before.ModifiedJulianDate);
        seconds = selector(before) + ((selector(after) - selector(before)) * fraction);
        return true;
    }
}
