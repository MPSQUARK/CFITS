namespace CFITS.Tests;

[FitsTable("FLOAT_VECTORS")]
public sealed class FloatVectorRow
{
    [FitsColumn("SAMPLES")]
    public float[] Samples { get; set; } = [];
}
