namespace CFITS.Tests.Serialization.Models;

public sealed partial class MixedEventTable
{
    public IReadOnlyList<MixedEventRow> Rows { get; set; } = [];
}
