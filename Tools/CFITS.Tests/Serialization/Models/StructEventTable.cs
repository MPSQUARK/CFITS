namespace CFITS.Tests.Serialization.Models;

public sealed partial class StructEventTable
{
    public IReadOnlyList<StructEventRow> Rows { get; set; } = [];
}
