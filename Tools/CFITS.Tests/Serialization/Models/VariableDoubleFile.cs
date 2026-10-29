namespace CFITS.Tests.Serialization.Models;

[FitsDocument(IgnoreOtherHdus = true)]
public sealed partial class VariableDoubleFile
{
    [FitsTable(1)]
    [FitsRequired]
    public VariableDoubleTable Table { get; set; } = null!;
}
