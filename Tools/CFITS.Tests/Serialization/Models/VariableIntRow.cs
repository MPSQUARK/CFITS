namespace CFITS.Tests.Serialization.Models;

public sealed class VariableIntRow
{
    [FitsColumn("VALUES")]
    [FitsRequired]
    public int[] Values { get; set; } = [];
}
