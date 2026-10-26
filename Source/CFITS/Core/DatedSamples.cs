namespace CFITS;

/// <summary>
/// Linear interpolation over a strictly increasing Modified Julian Date series.
/// Dates at or after the last sample keep that sample only when they compare equal;
/// instants outside the closed coverage return false.
/// </summary>
internal static class DatedSamples
{
    public static bool TryInterpolate(ReadOnlySpan<double> dates, ReadOnlySpan<double> values, double instant, out double result)
    {
        if (instant < dates[0] || instant > dates[^1])
        {
            result = 0;
            return false;
        }

        int lower = LastAtOrBefore(dates, instant);
        if (dates[lower] == instant)
        {
            result = values[lower];
            return true;
        }

        double before = dates[lower];
        double after = dates[lower + 1];
        double fraction = (instant - before) / (after - before);
        result = values[lower] + ((values[lower + 1] - values[lower]) * fraction);
        return true;
    }

    public static int LastAtOrBefore(ReadOnlySpan<double> dates, double instant)
    {
        int low = 0;
        int high = dates.Length - 1;
        int found = 0;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int taken = BoolMath.ToInt(instant >= dates[middle]);
            int rejected = 1 - taken;
            found = (taken * middle) + (rejected * found);
            low = (taken * (middle + 1)) + (rejected * low);
            high = (taken * high) + (rejected * (middle - 1));
        }

        return found;
    }
}
