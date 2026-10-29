namespace CFITS.Tests;

public sealed class GeneratedDocumentComplianceTests
{
    [Fact]
    public void GeneratedDocumentReadsWritesMappedAndUnknownHdus()
    {
        using var source = FitsDocumentBuilder.Create()
            .Image([1, 1], new[] { 1.5f }, header => header.Real("EXPTIME", 6.25))
            .Image([1], new byte[] { 7 }, header => header.Text("EXTNAME", "UNKNOWN"))
            .BinaryTable(
                new BinaryTableBuilder().Add("TIME", new[] { 10.5 }).Add("PHA", new[] { 4 }),
                header => header.Text("EXTNAME", "EVENTS"))
            .Build();

        InterleavedHduFile value;
        using (var input = new MemoryStream(source.ToArray()))
        {
            value = FitsSerializer.Read<InterleavedHduFile>(input);
        }

        Assert.Equal(6.25, value.Keywords.ExposureSeconds);
        Assert.Single(value.Events.Rows);
        Assert.Equal(4, value.Events.Rows[0].Channel);
        Assert.Single(value.Unknown);
        Assert.Equal(7, value.Unknown[0].DataBytes.Span[0]);

        using var output = new MemoryStream();
        FitsSerializer.Write(output, value);
        using var roundTrip = FitsDocument.Parse(output.ToArray());

        Assert.Equal(
            new[] { FitsHduKind.Image, FitsHduKind.Image, FitsHduKind.BinaryTable },
            roundTrip.Hdus.Select(hdu => hdu.Kind));
        Assert.Equal(7, roundTrip.Hdus[1].Image!.ReadInteger(0));
        Assert.Equal(4, roundTrip.Hdus[2].Table!.Column("PHA").ReadInteger(0));
    }
}
