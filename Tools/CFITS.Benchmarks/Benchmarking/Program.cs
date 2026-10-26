using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
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

        if (args.Contains("--replot"))
        {
            string repositoryRoot = FitsScenarios.FindRoot();
            Comparison.Replot(repositoryRoot);
            return;
        }

        if (args.Contains("--targeted"))
        {
            TargetedBenchmarks.Run();
            return;
        }

        if (args.Contains("--header-targeted"))
        {
            HeaderParserBenchmarks.Run();
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

        BenchmarkRunner.Run<FitsScenarios>(config, args);
    }
}
