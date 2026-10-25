namespace CFITS.Tests;

[FitsImage("LONG_EXPOSURE")]
public sealed class LongExposure
{
    [FitsData]
    public long[]? Pixels { get; set; }
}
