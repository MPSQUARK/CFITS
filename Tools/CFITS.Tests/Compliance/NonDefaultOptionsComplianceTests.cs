namespace CFITS.Tests;

public sealed class NonDefaultOptionsComplianceTests
{
    [Fact]
    public void GeneratedReadHonorsStoredScaleMode()
    {
        using var source = FitsDocumentBuilder.Create()
            .Image([1, 2], new short[] { -2, 3 }, header => header
                .Text("EXTNAME", "EXPOSURE")
                .Real("EXPTIME", 1)
                .Real("BSCALE", 2)
                .Real("BZERO", 10))
            .Build();

        using var stream = new MemoryStream(source.ToArray());
        ExposureFile result = FitsSerializer.Read<ExposureFile>(
            stream,
            new FitsOpenOptions { ScaleMode = FitsScaleMode.Stored });

        Assert.Equal(new[] { -2f, 3f }, result.Image.Pixels!.Cast<float>());
    }

    [Fact]
    public void GeneratedReadHonorsChecksumVerification()
    {
        using var source = FitsDocumentBuilder.Create()
            .Image([1], new[] { 1f })
            .Checksum()
            .Build();
        byte[] corrupted = source.ToArray();
        corrupted[^1] ^= 0x01;

        using var stream = new MemoryStream(corrupted);
        Assert.Throws<FitsException>(() => FitsSerializer.Read<ExposureFile>(
            stream,
            new FitsOpenOptions { VerifyChecksum = true }));
    }
}
