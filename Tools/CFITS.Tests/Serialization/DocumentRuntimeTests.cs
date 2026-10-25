namespace CFITS.Tests;

public sealed class DocumentRuntimeTests
{
    [Fact]
    public void DocumentSnapshotsOwnHduBytes()
    {
        using var document = FitsDocumentBuilder.Create().Image([2], new byte[] { 1, 2 }).Build();

        FitsHduSnapshot snapshot = document.SnapshotHdus()[0];
        byte[] expected = document.ToArray();
        using var stream = new MemoryStream();
        snapshot.Write(stream);

        Assert.Equal(expected, stream.ToArray());
        Assert.Equal(new byte[] { 1, 2 }, snapshot.DataBytes.ToArray());
    }

    [Fact]
    public void DocumentTypeInfoOpensWithRequestedOptions()
    {
        using var stream = new MemoryStream(
            FitsDocumentBuilder.Create().Image([1], new byte[] { 7 }).Build().ToArray());

        var type = new TestDocumentTypeInfo();
        Assert.Equal(7, type.Read(stream, new FitsOpenOptions()));
    }

    [Fact]
    public void ImageAndTableAttributesCanDecorateProperties()
    {
        Assert.NotNull(typeof(DocumentModel).GetProperty(nameof(DocumentModel.Image))!
            .GetCustomAttributes(typeof(FitsImageAttribute), inherit: false).Single());
        Assert.NotNull(typeof(DocumentModel).GetProperty(nameof(DocumentModel.Table))!
            .GetCustomAttributes(typeof(FitsTableAttribute), inherit: false).Single());
    }

    [FitsDocumentModel]
    private sealed class DocumentModel
    {
        [FitsImage("IMAGE")]
        public object? Image { get; set; }

        [FitsTable("TABLE")]
        public object? Table { get; set; }
    }

    private sealed class TestDocumentTypeInfo : FitsDocumentTypeInfo<int>
    {
        public override int Read(FitsDocument document) => (int)document.Hdus[0].Image!.ReadInteger(0);

        public override void Write(Stream stream, int value) => throw new NotSupportedException();
    }
}
