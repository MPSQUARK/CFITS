namespace CFITS;

internal static class DatedSampleTables
{
    public static (double[] Dates, double[] First, double[] Second) Split(
        (double ModifiedJulianDate, double First, double Second)[] samples)
    {
        var ordered = Copy(samples);
        Array.Sort(ordered, static (left, right) => left.ModifiedJulianDate.CompareTo(right.ModifiedJulianDate));
        var dates = new double[ordered.Length];
        var first = new double[ordered.Length];
        var second = new double[ordered.Length];
        for (int index = 0; index < ordered.Length; index++)
        {
            var sample = ordered[index];
            if (!double.IsFinite(sample.ModifiedJulianDate)
                || !double.IsFinite(sample.First)
                || !double.IsFinite(sample.Second)
                || (index > 0 && dates[index - 1] >= sample.ModifiedJulianDate))
            {
                throw new ArgumentException("Ephemeris samples must be finite and strictly increasing.", nameof(samples));
            }

            dates[index] = sample.ModifiedJulianDate;
            first[index] = sample.First;
            second[index] = sample.Second;
        }

        return (dates, first, second);
    }

    public static (double[] Dates, double[] Values) Split(
        (double ModifiedJulianDate, double Value)[] samples)
    {
        var ordered = Copy(samples);
        Array.Sort(ordered, static (left, right) => left.ModifiedJulianDate.CompareTo(right.ModifiedJulianDate));
        var dates = new double[ordered.Length];
        var values = new double[ordered.Length];
        for (int index = 0; index < ordered.Length; index++)
        {
            var sample = ordered[index];
            if (!double.IsFinite(sample.ModifiedJulianDate)
                || !double.IsFinite(sample.Value)
                || (index > 0 && dates[index - 1] >= sample.ModifiedJulianDate))
            {
                throw new ArgumentException("Earth-orientation samples must be finite and strictly increasing.", nameof(samples));
            }

            dates[index] = sample.ModifiedJulianDate;
            values[index] = sample.Value;
        }

        return (dates, values);
    }

    private static T[] Copy<T>(T[] samples)
    {
        if (samples is null || samples.Length == 0)
        {
            throw new ArgumentException("At least one sample is required.", nameof(samples));
        }

        var copy = new T[samples.Length];
        Array.Copy(samples, copy, samples.Length);
        return copy;
    }
}
