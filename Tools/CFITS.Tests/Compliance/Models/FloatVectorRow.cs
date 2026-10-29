namespace CFITS.Tests.Compliance.Models;

public sealed class FloatVectorRow
{
    [FitsColumn("SAMPLES")]
    [FitsRequired]
    public float[] Samples { get; set; } = [];
}
