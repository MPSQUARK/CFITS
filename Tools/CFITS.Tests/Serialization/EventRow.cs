namespace CFITS.Tests;

[FitsTable("EVENTS")]
public sealed class EventRow
{
    [FitsColumn("TIME")]
    public double Time { get; set; }

    [FitsColumn("PHA")]
    public int Channel { get; set; }
}
