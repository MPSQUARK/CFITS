namespace CFITS.Tests;

public sealed class CompressionTests
{
    [Fact]
    public void DecompressesZeroAxisCompressedImage()
    {
        using var document = FitsDocumentBuilder.Create()
            .GzipImage(Array.Empty<int>(), Array.Empty<short>())
            .Build();

        // The builder represents a zero-axis image with one empty compressed tile row.
        Assert.Equal(1, document.Hdus[1].Table?.RowCount);
        var image = document.Hdus[1].Image!;
        Assert.Empty(image.Axes);
        Assert.Equal(0, image.Length);
        Assert.Empty(image.ReadPhysical());
    }

    [Theory]
    [InlineData(15)]
    [InlineData(17)]
    public void RejectsMalformedLosslessGzipFallbackTiles(int decodedLength)
    {
        byte[] decoded = Enumerable.Range(0, decodedLength).Select(static value => (byte)value).ToArray();
        byte[] compressed = Gzip(decoded);
        string payload = new(compressed.Select(static value => (char)value).ToArray());

        var table = new BinaryTableBuilder()
            .AddVariable("COMPRESSED_DATA", new List<string> { "" })
            .AddVariable("GZIP_COMPRESSED_DATA", new List<string> { payload });

        Assert.Throws<FitsException>(() => FitsDocumentBuilder.Create()
            .BinaryTable(table, header => header
                .Logical("ZIMAGE", true)
                .Text("ZCMPTYPE", "GZIP_1")
                .Integer("ZBITPIX", -32)
                .Integer("ZNAXIS", 2)
                .Integer("ZNAXIS1", 2)
                .Integer("ZNAXIS2", 2)
                .Integer("ZTILE1", 2)
                .Integer("ZTILE2", 2))
            .Build());
    }

    [Fact]
    public void DecompressesTilesWrittenByAstropy()
    {
        var root = FindRoot();
        var python = PythonRuntime.Find(root);
        var directory = Path.Combine(root, "SampleData", "Compressed");
        Directory.CreateDirectory(directory);
        var generator = Path.Combine(root, "Tools", "CFITS.PyScripts", "Fixtures", "GenerateCompressionFixtures.py");
        var start = new System.Diagnostics.ProcessStartInfo(python)
        {
            WorkingDirectory = root,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(generator);
        start.ArgumentList.Add(directory);
        using var process = System.Diagnostics.Process.Start(start)!;
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);

        using var rice = FitsDocument.Open(Path.Combine(directory, "rice.fits"));
        Assert.Equal(FitsHduKind.Image, rice.Hdus[1].Kind);
        var riceValues = rice.Hdus[1].Image!.ReadPhysical();
        for (int index = 0; index < 64; index++)
        {
            Assert.Equal(index, riceValues[index]);
        }

        using var gzip = FitsDocument.Open(Path.Combine(directory, "gzip.fits"));
        var gzipValues = gzip.Hdus[1].Image!.ReadPhysical();
        for (int index = 0; index < 48; index++)
        {
            Assert.Equal(index, gzipValues[index]);
        }

        double[] expected = new double[42];
        for (int index = 0; index < expected.Length; index++)
        {
            expected[index] = index - 20;
        }

        using var hcomp = FitsDocument.Open(Path.Combine(directory, "hcomp.fits"));
        Assert.Equal(expected, hcomp.Hdus[1].Image!.ReadPhysical());

        var smoothExpected = File.ReadAllLines(Path.Combine(directory, "hcomp-smooth.txt")).Select(line => double.Parse(line, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        using var smooth = FitsDocument.Open(Path.Combine(directory, "hcomp-smooth.fits"));
        Assert.Equal(smoothExpected, smooth.Hdus[1].Image!.ReadPhysical());

        using var floats = FitsDocument.Open(Path.Combine(directory, "gzip2.fits"));
        var values = floats.Hdus[1].Image!.ReadPhysical();
        Assert.Equal(1.5, values[0]);
        Assert.True(double.IsNaN(values[1]));
        Assert.Equal(-2.25, values[2]);
        Assert.Equal(4, values[3]);

        using var tiled = FitsDocument.Open(Path.Combine(directory, "tiled.fits"));
        Assert.Equal(Enumerable.Range(0, 35).Select(static value => (double)value).ToArray(), tiled.Hdus[1].Image!.ReadPhysical());
    }

    [Fact]
    public void WritesGzipTilesTheRuntimeCanCompress()
    {
        short[] pixels = [1, -2, 3, 4, 5, 6];
        using var document = FitsDocumentBuilder.Create()
            .GzipImage([3, 2], pixels)
            .Build();
        var image = document.Hdus[1].Image!;
        Assert.Equal(pixels.Select(value => (double)value).ToArray(), image.ReadPhysical());

        float[] samples = [1.5f, float.NaN, -2.25f, 4f];
        using var shuffled = FitsDocumentBuilder.Create()
            .GzipImage([2, 2], samples, shuffle: true)
            .Build();
        var values = shuffled.Hdus[1].Image!.ReadPhysical();
        Assert.Equal(1.5, values[0]);
        Assert.True(double.IsNaN(values[1]));
        Assert.Equal(-2.25, values[2]);
        Assert.Equal(4, values[3]);
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

    private static byte[] Gzip(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(bytes);
        }

        return output.ToArray();
    }
}
