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
    public void LosslessGzipFallbackPreservesDecodedBytes()
    {
        byte[] expected =
        [
            0x80, 0xFE,
            0x00, 0xFF,
        ];
        byte[] compressed = Gzip(expected);
        string payload = new(compressed.Select(static value => (char)value).ToArray());

        var table = new BinaryTableBuilder()
            .AddVariable("COMPRESSED_DATA", new List<string> { "" })
            .AddVariable("GZIP_COMPRESSED_DATA", new List<string> { payload });

        using var document = FitsDocumentBuilder.Create()
            .BinaryTable(table, header => header
                .Logical("ZIMAGE", true)
                .Text("ZCMPTYPE", "GZIP_1")
                .Integer("ZBITPIX", 8)
                .Integer("ZNAXIS", 2)
                .Integer("ZNAXIS1", 2)
                .Integer("ZNAXIS2", 2)
                .Integer("ZTILE1", 2)
                .Integer("ZTILE2", 2))
            .Build();

        byte[] reference = GunzipReference(compressed);
        Assert.Equal(reference, expected);
        byte[] actual = new byte[expected.Length];
        document.Hdus[1].Image!.CopyInteger(actual);
        Assert.Equal(reference, actual);
    }

    [Fact]
    public void Gzip2UnshufflePreservesStoredBytes()
    {
        byte[] expected =
        [
            0x01, 0x02, 0x03, 0x04,
            0x80, 0x81, 0x82, 0x83,
            0xFE, 0xFD, 0xFC, 0xFB,
        ];
        byte[] shuffled =
        [
            0x01, 0x80, 0xFE,
            0x02, 0x81, 0xFD,
            0x03, 0x82, 0xFC,
            0x04, 0x83, 0xFB,
        ];
        string payload = new(Gzip(shuffled).Select(static value => (char)value).ToArray());

        using var document = FitsDocumentBuilder.Create()
            .BinaryTable(
                new BinaryTableBuilder().AddVariable("COMPRESSED_DATA", new[] { payload }),
                header => header
                    .Logical("ZIMAGE", true)
                    .Text("ZCMPTYPE", "GZIP_2")
                    .Integer("ZBITPIX", 32)
                    .Integer("ZNAXIS", 1)
                    .Integer("ZNAXIS1", 3)
                    .Integer("ZTILE1", 3))
            .Build();

        int[] actual = new int[expected.Length / sizeof(int)];
        document.Hdus[1].Image!.CopyInteger(actual);
        Assert.Equal(
            [
                0x01020304,
                unchecked((int)0x80818283),
                unchecked((int)0xFEFDFCFB),
            ],
            actual);
    }

    [Fact]
    public void RejectsGzip2TileWithoutWholeEncodedPixels()
    {
        string payload = new(Gzip([1, 2, 3]).Select(static value => (char)value).ToArray());

        Assert.Throws<FitsException>(() => FitsDocumentBuilder.Create()
            .BinaryTable(
                new BinaryTableBuilder().AddVariable("COMPRESSED_DATA", new[] { payload }),
                header => header
                    .Logical("ZIMAGE", true)
                    .Text("ZCMPTYPE", "GZIP_2")
                    .Integer("ZBITPIX", 32)
                    .Integer("ZNAXIS", 1)
                    .Integer("ZNAXIS1", 2)
                    .Integer("ZTILE1", 2))
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
    public void RiceFixtureMatchesIndependentReferenceDecoder()
    {
        using var document = FitsDocument.Open(FixturePath("SampleData/Compressed/rice.fits"));
        FitsTable table = document.Hdus[1].Table!;
        short[] actual = new short[64];
        document.Hdus[1].Image!.CopyInteger(actual);

        for (int row = 0; row < 8; row++)
        {
            byte[] compressed = table.Column("COMPRESSED_DATA").ReadVariableBytes(row);
            uint[] expected = DecodeRiceReference(compressed, pixels: 8, byteWidth: 2, blockSize: 32);
            Assert.Equal(expected.Select(static value => unchecked((short)value)), actual[(row * 8)..((row + 1) * 8)]);
        }

        Assert.Equal(Enumerable.Range(0, 64).Select(static value => (short)value), actual);
    }

    [Fact]
    public void RiceRejectsEveryTruncatedFixturePayload()
    {
        using var source = FitsDocument.Open(FixturePath("SampleData/Compressed/rice.fits"));
        byte[] compressed = source.Hdus[1].Table!.Column("COMPRESSED_DATA").ReadVariableBytes(0);

        for (int length = 0; length < compressed.Length; length++)
        {
            byte[] truncated = compressed[..length];
            Assert.Throws<FitsException>(() => BuildRiceDocument(truncated));
        }
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

    private static string FixturePath(string relativePath) =>
        Path.Combine(FindRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static byte[] Gzip(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(bytes);
        }

        return output.ToArray();
    }

    private static FitsDocument BuildRiceDocument(byte[] compressed)
    {
        string payload = new(compressed.Select(static value => (char)value).ToArray());
        return FitsDocumentBuilder.Create()
            .BinaryTable(
                new BinaryTableBuilder().AddVariable("COMPRESSED_DATA", new[] { payload }),
                header => header
                    .Logical("ZIMAGE", true)
                    .Text("ZCMPTYPE", "RICE_1")
                    .Integer("ZBITPIX", 16)
                    .Integer("ZNAXIS", 2)
                    .Integer("ZNAXIS1", 8)
                    .Integer("ZNAXIS2", 1)
                    .Integer("ZTILE1", 8)
                    .Integer("ZTILE2", 1))
            .Build();
    }

    private static uint[] DecodeRiceReference(byte[] source, int pixels, int byteWidth, int blockSize)
    {
        int fsBits = byteWidth switch { 1 => 3, 2 => 4, 4 => 5, _ => throw new ArgumentOutOfRangeException(nameof(byteWidth)) };
        int fsMax = byteWidth switch { 1 => 6, 2 => 14, _ => 25 };
        int directBits = 1 << fsBits;
        uint mask = byteWidth == 4 ? uint.MaxValue : (1u << (byteWidth * 8)) - 1;
        int bit = 0;
        uint last = 0;
        for (int index = 0; index < byteWidth; index++)
            last = (last << 8) | ReadBits(source, ref bit, 8);

        var output = new uint[pixels];
        int pixel = 0;
        while (pixel < pixels)
        {
            int fs = (int)ReadBits(source, ref bit, fsBits) - 1;
            int limit = Math.Min(pixel + blockSize, pixels);
            if (fs < 0)
            {
                while (pixel < limit)
                    output[pixel++] = last;
                continue;
            }

            while (pixel < limit)
            {
                uint mapped;
                if (fs == fsMax)
                {
                    mapped = ReadBits(source, ref bit, directBits);
                }
                else
                {
                    int zeros = 0;
                    while (ReadBits(source, ref bit, 1) == 0)
                        zeros++;
                    mapped = ((uint)zeros << fs) | ReadBits(source, ref bit, fs);
                }

                last = (last + UnmapReference(mapped)) & mask;
                output[pixel++] = last;
            }
        }

        return output;
    }

    private static uint ReadBits(byte[] source, ref int bit, int count)
    {
        if (count == 0)
            return 0;
        if (bit > (source.Length * 8) - count)
            throw new FitsException("Reference Rice stream ended.", new FitsLocation());

        uint value = 0;
        for (int index = 0; index < count; index++)
        {
            value = (value << 1) | (uint)((source[bit >> 3] >> (7 - (bit & 7))) & 1);
            bit++;
        }

        return value;
    }

    private static uint UnmapReference(uint value) => (value & 1) == 0 ? value >> 1 : ~(value >> 1);

    private static byte[] GunzipReference(byte[] compressed)
    {
        using var input = new MemoryStream(compressed, writable: false);
        using var gzip = new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }
}
