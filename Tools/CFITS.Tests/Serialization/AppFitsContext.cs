using System.Buffers;

namespace CFITS.Tests;

[FitsSerializable(typeof(Exposure))]
[FitsSerializable(typeof(EventRow))]
public sealed partial class AppFitsContext : FitsSerializerContext;
