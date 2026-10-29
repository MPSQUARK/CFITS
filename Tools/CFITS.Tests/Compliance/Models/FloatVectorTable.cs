namespace CFITS.Tests.Compliance.Models;

public sealed partial class FloatVectorTable
{
    public IReadOnlyList<FloatVectorRow> Rows { get; set; } = [];
}
