namespace CFITS.Tests;

[FitsTable]
public struct StructEventRow
{
    [FitsColumn("TIME")]
    public double Time { get; set; }

    [FitsColumn("PHA")]
    public int Channel { get; set; }
}
