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
    public void SnapshotWithIndexRetainsOwnedBytes()
    {
        using var document = FitsDocumentBuilder.Create().Image([2], new byte[] { 3, 4 }).Build();
        FitsHduSnapshot original = document.SnapshotHdus()[0];

        FitsHduSnapshot renamed = original.WithIndex(7);

        Assert.Equal(7, renamed.Index);
        Assert.Equal(original.HeaderBytes.ToArray(), renamed.HeaderBytes.ToArray());
        Assert.Equal(original.DataBytes.ToArray(), renamed.DataBytes.ToArray());
    }

    [Fact]
    public void ParseOwnsInputMemory()
    {
        byte[] bytes = FitsDocumentBuilder.Create().Image([1], new byte[] { 9 }).Build().ToArray();
        using var document = FitsDocument.Parse(bytes);
        Array.Fill(bytes, (byte)0);

        Assert.Equal(9, document.Hdus[0].Image!.ReadInteger(0));
    }

    [Fact]
    public void BuilderBuildCanBeWrittenAfterBuilderScopeEnds()
    {
        FitsDocument document;
        {
            var builder = FitsDocumentBuilder.Create().Image([1], new byte[] { 5 });
            document = builder.Build();
        }

        using (document)
        {
            Assert.Equal(new byte[] { 5 }, document.Hdus[0].RawData.ToArray());
        }
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
