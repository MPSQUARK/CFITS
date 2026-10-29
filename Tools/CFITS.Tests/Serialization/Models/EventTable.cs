namespace CFITS.Tests.Serialization.Models;

public sealed partial class EventTable
{
    public IReadOnlyList<EventRow> Rows { get; set; } = [];
}
