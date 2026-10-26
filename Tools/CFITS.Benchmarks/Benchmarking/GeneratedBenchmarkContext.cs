namespace CFITS.Benchmarks;

[FitsSerializable(typeof(GeneratedExposure))]
[FitsSerializable(typeof(GeneratedEventRow))]
[FitsSerializable(typeof(GeneratedShortImage))]
[FitsSerializable(typeof(GeneratedFloatImage))]
[FitsSerializable(typeof(GeneratedFluxRow))]
[FitsSerializable(typeof(GeneratedAsciiRow))]
[FitsSerializable(typeof(GeneratedVariableRow))]
[FitsSerializable(typeof(GeneratedVariableDoubleRow))]
[FitsSerializable(typeof(GeneratedVariableIntRow))]
[FitsSerializable(typeof(GeneratedCompressedImage))]
[FitsSerializable(typeof(GeneratedCompressedShortImage))]
[FitsSerializable(typeof(GeneratedWholeFile))]
[FitsSerializable(typeof(GeneratedCompositeFile))]
public sealed partial class GeneratedBenchmarkContext : FitsSerializerContext;
