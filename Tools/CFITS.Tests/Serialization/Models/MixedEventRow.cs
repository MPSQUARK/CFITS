namespace CFITS.Tests.Serialization.Models;

public sealed class MixedEventRow
{
    [FitsColumn("TIME")]
    [FitsRequired]
    public double Time { get; set; }

    [FitsColumn("LABEL")]
    [FitsRequired]
    public string Label { get; set; } = "";

    [FitsColumn("PHA")]
    [FitsRequired]
    public int Channel { get; set; }
}
