namespace CFITS.Tests;

[FitsSerializable(typeof(Exposure))]
[FitsSerializable(typeof(ByteKeywordImage))]
[FitsSerializable(typeof(EventRow))]
[FitsSerializable(typeof(MixedEventRow))]
[FitsSerializable(typeof(LongExposure))]
[FitsSerializable(typeof(UnsupportedImage))]
[FitsSerializable(typeof(ScalarImage))]
[FitsSerializable(typeof(UnsupportedTableRow))]
[FitsSerializable(typeof(VariableDoubleRow))]
[FitsSerializable(typeof(VariableIntRow))]
[FitsSerializable(typeof(MultiHduFile))]
[FitsSerializable(typeof(InterleavedHduFile))]
[FitsSerializable(typeof(DirectCompositeFile))]
public sealed partial class AppFitsContext : FitsSerializerContext;
