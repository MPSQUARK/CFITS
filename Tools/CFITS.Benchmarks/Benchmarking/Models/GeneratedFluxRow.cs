namespace CFITS.Benchmarks.Benchmarking.Models;

public struct GeneratedFluxRow
{
    [FitsColumn("flux")]
    [FitsRequired]
    public double Flux { get; set; }
}
