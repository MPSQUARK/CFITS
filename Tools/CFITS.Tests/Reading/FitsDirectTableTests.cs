namespace CFITS.Tests;

public sealed class FitsDirectTableTests
{
    [Fact]
    public void ReadsOnlyTheRequestedLaterColumn()
    {
        byte[] file = FitsDocumentBuilder.Create()
            .BinaryTable(new BinaryTableBuilder()
                .Add("first", new double[] { 1, 2 })
                .Add("second", new[] { 3, 4 })
                .Add("third", new[] { 5, 6 }))
            .Build()
            .ToArray();
        using var stream = new MemoryStream(file);
        Assert.True(FitsDirectTable.TryOpen(stream, -1, null, new FitsOpenOptions(), ["third"], out FitsDirectTable? opened));
        using (opened)
        {
            var values = new int[2];
            Assert.True(opened!.Table.Column("third").TryCopyInteger(values));
            Assert.Equal(new[] { 5, 6 }, values);
            Assert.Throws<FitsException>(() => opened.Table.Column("first"));
        }
    }

    [Fact]
    public void ReadsAnAsciiColumnFromItsCharacterField()
    {
        byte[] file = FitsDocumentBuilder.Create()
            .AsciiTable(new AsciiTableBuilder().AddReal("value", new[] { 1.5, 2.5 }, 10, 2, 'F'))
            .Build()
            .ToArray();
        using var stream = new MemoryStream(file);
        Assert.True(FitsDirectTable.TryOpen(stream, -1, null, new FitsOpenOptions(), ["value"], out FitsDirectTable? opened));
        using (opened)
        {
            Assert.Equal(1.5, opened!.Table.Column("value").ReadReal(0));
            Assert.Equal(2.5, opened.Table.Column("value").ReadReal(1));
        }
    }
}
