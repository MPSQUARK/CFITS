using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace CFITS.Benchmarks;

public static class Program
{
    public static void Main(string[] args)
    {
        if (args.Contains("--probe"))
        {
            Comparison.Probe();
            return;
        }

        if (args.Contains("--compare"))
        {
            Comparison.Run();
            return;
        }

        var job = Job.Default
            .WithWarmupCount(2)
            .WithIterationCount(6)
            .WithLaunchCount(1)
            .WithId("baseline");
        var config = ManualConfig.Create(DefaultConfig.Instance)
            .AddJob(job)
            .WithOptions(
                ConfigOptions.JoinSummary |
                ConfigOptions.DisableLogFile |
                ConfigOptions.DisableOptimizationsValidator);

        Summary summary = BenchmarkRunner.Run<FitsScenarios>(config, args);
        string root = FitsScenarios.FindRoot();
        string png = Path.Combine(root, "benchmarks", "fits-scenarios.png");
        string csv = ScenarioPlot.Save(summary, png);
        Console.WriteLine($"Plot: {png}");
        Console.WriteLine($"Table: {csv}");
    }
}
