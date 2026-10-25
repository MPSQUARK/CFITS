namespace CFITS.Tests;

public sealed class AsciiTableComplianceTests
{
    [Fact]
    public void RejectsValuesThatOverflowTheirAsciiFields()
    {
        Assert.Throws<FitsException>(() =>
            new AsciiTableBuilder().AddInteger("VALUE", [12345], 4));

        Assert.Throws<FitsException>(() =>
            new AsciiTableBuilder().AddText("VALUE", ["abcd"], 3));
    }

    [Fact]
    public void RejectsInvalidAsciiPrecisionAndWidth()
    {
        Assert.Throws<FitsException>(() =>
            FitsDocumentBuilder.Create()
                .AsciiTable(
                    new AsciiTableBuilder().AddInteger("VALUE", [7], 3),
                    header => header.Text("TFORM1", "F3."))
                .Build());

        Assert.Throws<FitsException>(() =>
            FitsDocumentBuilder.Create()
                .AsciiTable(
                    new AsciiTableBuilder().AddInteger("VALUE", [7], 3),
                    header => header.Text("TFORM1", "I0"))
                .Build());
    }

    [Fact]
    public void RejectsOverlappingAsciiColumns()
    {
        Assert.Throws<FitsException>(() =>
            FitsDocumentBuilder.Create()
                .AsciiTable(
                    new AsciiTableBuilder()
                        .AddText("LEFT", ["abc"], 5)
                        .AddInteger("RIGHT", [7], 5),
                    header => header.Integer("TBCOL2", 3))
                .Build());
    }

    [Fact]
    public void RejectsTdimKeywordInAsciiTable()
    {
        Assert.Throws<FitsException>(() =>
            FitsDocumentBuilder.Create()
                .AsciiTable(
                    new AsciiTableBuilder().AddInteger("VALUE", [7], 5),
                    header => header.Text("TDIM1", "(1)"))
                .Build());
    }
}
