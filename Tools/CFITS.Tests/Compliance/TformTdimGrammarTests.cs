namespace CFITS.Tests;

public sealed class TformTdimGrammarTests
{
    [Theory]
    [InlineData("1L")]
    [InlineData("1X")]
    [InlineData("1A")]
    [InlineData("1B")]
    [InlineData("1I")]
    [InlineData("1J")]
    [InlineData("1K")]
    [InlineData("1E")]
    [InlineData("1D")]
    [InlineData("1C")]
    public void AcceptsCompleteBinaryTformCodeSet(string format)
    {
        using var document = FitsDocumentBuilder.Create()
            .BinaryTable(
                new BinaryTableBuilder().Add("VALUE", new[] { 1.0 }),
                header => header.Text("TFORM1", format))
            .Build();

        Assert.Equal(format, document.Hdus[1].Table!.Column("VALUE").Format);
    }

    [Fact]
    public void AcceptsVariableDescriptorElementCodes()
    {
        using var document = FitsDocumentBuilder.Create()
            .BinaryTable(
                new BinaryTableBuilder().AddVariable("VALUE", new[] { new[] { 1 } }),
                header => header.Text("TFORM1", "1PJ"))
            .Build();

        Assert.Equal("1PJ", document.Hdus[1].Table!.Column("VALUE").Format);
    }

    [Fact]
    public void AcceptsWideVariableDescriptor()
    {
        using var document = FitsDocumentBuilder.Create()
            .BinaryTable(
                new BinaryTableBuilder().AddVariable("VALUE", new[] { new[] { 1.0 } }),
                header => header.Text("TFORM1", "1QD"))
            .Build();

        Assert.Equal("1QD", document.Hdus[1].Table!.Column("VALUE").Format);
    }

    [Theory]
    [InlineData("P")]
    [InlineData("1Z")]
    [InlineData("1PZ")]
    [InlineData("1PJJ")]
    [InlineData("0J")]
    [InlineData("1A0")]
    public void RejectsInvalidBinaryTformGrammar(string format)
    {
        Assert.Throws<FitsException>(() =>
            FitsDocumentBuilder.Create()
                .BinaryTable(
                    new BinaryTableBuilder().Add("VALUE", new[] { 1 }),
                    header => header.Text("TFORM1", format))
                .Build());
    }

    [Fact]
    public void RejectsVariableTformWithoutElementCode()
    {
        Assert.Throws<FitsException>(() =>
            FitsDocumentBuilder.Create()
                .BinaryTable(
                    new BinaryTableBuilder().AddVariable("VALUE", new[] { new[] { 1 } }),
                    header => header.Text("TFORM1", "P"))
                .Build());
    }

}
