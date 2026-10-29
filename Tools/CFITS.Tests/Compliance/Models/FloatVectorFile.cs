namespace CFITS.Tests.Compliance.Models;

[FitsDocument(IgnoreOtherHdus = true)]
public sealed partial class FloatVectorFile
{
    [FitsTable(1, "FLOAT_VECTORS")]
    [FitsRequired]
    public FloatVectorTable Table { get; set; } = null!;
}
