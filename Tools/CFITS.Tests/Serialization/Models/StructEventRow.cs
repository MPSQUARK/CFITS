namespace CFITS.Tests.Serialization.Models;

public struct StructEventRow
{
    [FitsColumn("TIME")]
    [FitsRequired]
    public double Time { get; set; }

    [FitsColumn("PHA")]
    [FitsRequired]
    public int Channel { get; set; }
}
