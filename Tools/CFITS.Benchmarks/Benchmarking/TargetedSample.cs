namespace CFITS.Benchmarks;

internal readonly record struct TargetedSample(
    double MeanNanoseconds,
    double StandardDeviationNanoseconds,
    double MeanAllocatedBytes,
    double StandardDeviationAllocatedBytes);
