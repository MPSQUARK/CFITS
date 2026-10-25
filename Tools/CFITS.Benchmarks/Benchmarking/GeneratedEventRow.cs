namespace CFITS.Benchmarks;

[FitsTable("EVENTS")]
public sealed class GeneratedEventRow
{
    [FitsColumn("TIME")]
    public double Time { get; set; }

    [FitsColumn("PHA")]
    public int Channel { get; set; }
}
