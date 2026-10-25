namespace CFITS.Benchmarks;

[FitsSerializable(typeof(GeneratedExposure))]
[FitsSerializable(typeof(GeneratedEventRow))]
[FitsSerializable(typeof(GeneratedShortImage))]
[FitsSerializable(typeof(GeneratedFloatImage))]
[FitsSerializable(typeof(GeneratedFluxRow))]
[FitsSerializable(typeof(GeneratedAsciiRow))]
[FitsSerializable(typeof(GeneratedVariableRow))]
[FitsSerializable(typeof(GeneratedCompressedImage))]
[FitsSerializable(typeof(GeneratedAnyRow))]
public sealed partial class GeneratedBenchmarkContext : FitsSerializerContext;
