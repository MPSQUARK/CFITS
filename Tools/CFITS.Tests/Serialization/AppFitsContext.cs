namespace CFITS.Tests;

[FitsSerializable(typeof(Exposure))]
[FitsSerializable(typeof(EventRow))]
[FitsSerializable(typeof(MixedEventRow))]
[FitsSerializable(typeof(LongExposure))]
[FitsSerializable(typeof(UnsupportedImage))]
[FitsSerializable(typeof(ScalarImage))]
[FitsSerializable(typeof(UnsupportedTableRow))]
[FitsSerializable(typeof(MultiHduFile))]
[FitsSerializable(typeof(InterleavedHduFile))]
public sealed partial class AppFitsContext : FitsSerializerContext;
