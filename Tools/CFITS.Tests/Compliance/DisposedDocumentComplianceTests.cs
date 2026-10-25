namespace CFITS.Tests;

public sealed class DisposedDocumentComplianceTests
{
    [Fact]
    public void DisposedDocumentRejectsAllDocumentOperations()
    {
        using var document = FitsDocumentBuilder.Create()
            .Image([1], new byte[] { 5 })
            .Build();
        document.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _ = document.Hdus);
        Assert.Throws<ObjectDisposedException>(() => document.SnapshotHdus());
        Assert.Throws<ObjectDisposedException>(() => document.SnapshotSpecialRecords());
        Assert.Throws<ObjectDisposedException>(() => document.Write(new MemoryStream()));
        Assert.Throws<ObjectDisposedException>(() => document.ToArray());
        Assert.Throws<ObjectDisposedException>(() => document.Save(
            Path.Combine(Path.GetTempPath(), $"cfits-disposed-{Guid.NewGuid():N}.fits")));
    }
}
