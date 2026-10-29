namespace CFITS.Tests.Serialization.Models;

[FitsDocument(IgnoreOtherHdus = true)]
public sealed partial class VariableIntFile
{
    [FitsTable(1)]
    [FitsRequired]
    public VariableIntTable Table { get; set; } = null!;
}
