namespace CFITS.Tests;

public sealed class VariableArrayComplianceTests
{
    [Fact]
    public void RejectsTheapInsideFixedRows()
    {
        Assert.Throws<FitsException>(() =>
            FitsDocumentBuilder.Create()
                .BinaryTable(
                    new BinaryTableBuilder().AddVariable("VALUES", new[] { new[] { 1, 2 } }),
                    header => header.Integer("THEAP", 0))
                .Build());
    }

    [Fact]
    public void RejectsWideDescriptorHeapOverlap()
    {
        Assert.Throws<FitsException>(() =>
            FitsDocumentBuilder.Create()
                .BinaryTable(
                    new BinaryTableBuilder().AddVariable("VALUES", new[] { new[] { 1.0 } }),
                    header => header.Integer("THEAP", 0))
                .Build());
    }
}
