namespace CFITS.Benchmarks.Models;

public sealed class GeneratedEventRow
{
    [FitsColumn("TIME")]
    [FitsRequired]
    public double Time { get; set; }

    [FitsColumn("PHA")]
    [FitsRequired]
    public int Channel { get; set; }
}
