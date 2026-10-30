namespace CFITS.Benchmarks.Models;

public struct GeneratedFluxRow
{
    [FitsColumn("flux")]
    [FitsRequired]
    public double Flux { get; set; }
}
