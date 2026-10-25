namespace CFITS.Tests;

[FitsSerializable(typeof(Exposure))]
[FitsSerializable(typeof(EventRow))]
[FitsSerializable(typeof(LongExposure))]
[FitsSerializable(typeof(UnsupportedImage))]
[FitsSerializable(typeof(ScalarImage))]
[FitsSerializable(typeof(UnsupportedTableRow))]
public sealed partial class AppFitsContext : FitsSerializerContext;
