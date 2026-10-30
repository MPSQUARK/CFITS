namespace CFITS.Tests;

/// <summary>
/// BOYB (bring your own buffer): decode image HDUs into caller-owned spans via <see cref="FitsImage.Read{T}"/>.
/// </summary>
public sealed class BoybReadTests
{
    [Fact(DisplayName = "BOYB: packed float image matches rectangular CopyPhysical")]
    public void ReadsPackedFloatImageInFitsOrder()
    {
        float[] source = [1f, 2f, 3f, 4f, 5f, 6f];
        byte[] file = FitsDocumentBuilder.Create().Image([2, 3], source).Build().ToArray();
        using var stream = new MemoryStream(file);

        FitsImageLayout layout = FitsImage.Describe(stream);
        Assert.Equal(0, stream.Position);
        Assert.Equal([2, 3], layout.Axes);
        Assert.Equal(6, layout.PixelCount);
        Assert.Equal(-32, layout.Bitpix);
        Assert.False(layout.Compressed);

        var packed = new float[layout.PixelCount];
        FitsImageLayout read = FitsImage.Read<float>(stream, packed);
        Assert.Equal(source, packed);
        Assert.Equal(layout.Axes, read.Axes);

        using var document = FitsDocument.Open(new MemoryStream(file));
        var rectangular = new float[3, 2];
        document.Hdus[0].Image!.CopyPhysical(rectangular);
        Assert.Equal(source, FitsEndian.AsContiguous(rectangular).ToArray());
    }

    [Fact(DisplayName = "BOYB: packed short image in NAXIS1-fastest order")]
    public void ReadsPackedShortImageInFitsOrder()
    {
        short[] source = [10, 20, 30, 40];
        byte[] file = FitsDocumentBuilder.Create().Image([2, 2], source).Build().ToArray();
        using var stream = new MemoryStream(file);
        var packed = new short[4];

        FitsImageLayout layout = FitsImage.Read<short>(stream, packed);

        Assert.Equal([2, 2], layout.Axes);
        Assert.Equal(source, packed);
    }

    [Fact(DisplayName = "BOYB: BSCALE, BZERO, and BLANK applied in the caller buffer")]
    public void AppliesPhysicalScaleAndBlankToTheCallerBuffer()
    {
        byte[] scaled = FitsDocumentBuilder.Create()
            .Image([2, 2], new short[] { 1, 2, 3, 4 }, header => header.Real("BSCALE", 2).Real("BZERO", 1))
            .Build()
            .ToArray();
        using var scaledStream = new MemoryStream(scaled);
        var physical = new float[4];
        FitsImage.Read<float>(scaledStream, physical);
        Assert.Equal([3f, 5f, 7f, 9f], physical);

        byte[] blanked = FitsDocumentBuilder.Create()
            .Image([3], new short[] { 1, -999, 2 }, header => header.Integer("BLANK", -999))
            .Build()
            .ToArray();
        using var blankStream = new MemoryStream(blanked);
        var values = new float[3];
        FitsImage.Read<float>(blankStream, values);
        Assert.Equal(1f, values[0]);
        Assert.True(float.IsNaN(values[1]));
        Assert.Equal(2f, values[2]);
    }

    [Fact(DisplayName = "BOYB: short destination rejects read and restores stream position")]
    public void RejectsAShortBufferAndRestoresTheStream()
    {
        byte[] file = FitsDocumentBuilder.Create().Image([4, 2], new float[8]).Build().ToArray();
        using var stream = new MemoryStream(file);
        var packed = new float[7];

        FitsException exception = Assert.Throws<FitsException>(() => FitsImage.Read<float>(stream, packed));

        Assert.Contains("8", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, stream.Position);
    }

    [Fact(DisplayName = "BOYB: Rice compressed HDU matches FitsDirectImage rectangular read")]
    public void ReadsRiceImageIntoTheSameOrderAsTheRectangularImage()
    {
        string path = Path.Combine(FindRoot(), "Benchmarks", "Data", "rice.fits");
        byte[] file = File.ReadAllBytes(path);
        using var described = new MemoryStream(file);
        FitsImageLayout layout = FitsImage.Describe(described, hduIndex: 1);
        Assert.Equal(0, described.Position);
        Assert.True(layout.Compressed);
        Assert.Equal(2, layout.Axes.Count);
        Assert.Equal(layout.Axes[0] * layout.Axes[1], layout.PixelCount);

        using var packedStream = new MemoryStream(file);
        var packed = new short[layout.PixelCount];
        FitsImage.Read<short>(packedStream, packed, hduIndex: 1);

        using var rectangularStream = new MemoryStream(file);
        Assert.True(FitsDirectImage.TryRead<short>(rectangularStream, 2, 1, null, new FitsOpenOptions(), out Array? pixels));
        short[] expected = FitsEndian.AsContiguous((short[,])pixels!).ToArray();
        Assert.Equal(expected, packed);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CFITS.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("The CFITS repository root was not found.");
    }
}
