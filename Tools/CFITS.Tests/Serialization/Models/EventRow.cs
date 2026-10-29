namespace CFITS.Tests.Serialization.Models;

public sealed class EventRow
{
    [FitsColumn("TIME")]
    [FitsRequired]
    public double Time { get; set; }

    [FitsColumn("PHA")]
    [FitsRequired]
    public int Channel { get; set; }
}
