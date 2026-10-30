namespace CFITS.Benchmarks.Models;

public struct GeneratedAsciiRow
{
    [FitsColumn("value")]
    [FitsRequired]
    public double Value { get; set; }
}
