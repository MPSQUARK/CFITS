namespace CFITS.Tests;

[FitsTable("MIXED_EVENTS")]
public sealed class MixedEventRow
{
    [FitsColumn("TIME")]
    public double Time { get; set; }

    [FitsColumn("LABEL")]
    public string Label { get; set; } = "";

    [FitsColumn("PHA")]
    public int Channel { get; set; }
}
