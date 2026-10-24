using BenchmarkDotNet.Attributes;

namespace CFITS.Benchmarks;

/// <summary>
/// Baseline timings for the current reader and writers. Each method is one scenario.
/// </summary>
[MemoryDiagnoser]
public class FitsScenarios
{
    private string _tiny = "";
    private string _image256 = "";
    private string _image1024 = "";
    private string _binary = "";
    private string _ascii = "";
    private string _heap = "";
    private string _gzip = "";
    private string _gzipShuffled = "";
    private string _checksum = "";
    private string _rice = "";
    private string _hcompress = "";
    private string? _sdss;
    private FitsWorld _world = null!;
    private readonly double[] _pixel = new double[2];
    private readonly double[] _worldPixel = new double[2];

    [GlobalSetup]
    public void Prepare()
    {
        string root = FindRoot();
        string directory = Path.Combine(root, "Benchmarks", "Data");
        Directory.CreateDirectory(directory);

        _tiny = Path.Combine(directory, "tiny.fits");
        FitsDocumentBuilder.Create().Image([4, 4], Enumerable.Range(0, 16).Select(i => (short)i).ToArray()).Build().Save(_tiny);

        _image256 = Path.Combine(directory, "image256.fits");
        short[] small = new short[256 * 256];
        for (int i = 0; i < small.Length; i++)
        {
            small[i] = (short)(i % 1000);
        }

        FitsDocumentBuilder.Create().Image([256, 256], small).Build().Save(_image256);

        _image1024 = Path.Combine(directory, "image1024.fits");
        float[] large = new float[1024 * 1024];
        for (int i = 0; i < large.Length; i++)
        {
            large[i] = i * 0.001f;
        }

        FitsDocumentBuilder.Create().Image([1024, 1024], large).Build().Save(_image1024);

        int rows = 20_000;
        double[] flux = new double[rows];
        int[] flags = new int[rows];
        for (int i = 0; i < rows; i++)
        {
            flux[i] = i * 0.01;
            flags[i] = i % 7;
        }

        _binary = Path.Combine(directory, "bintable.fits");
        FitsDocumentBuilder.Create()
            .BinaryTable(new BinaryTableBuilder().Add("flux", flux).Add("flags", flags))
            .Build()
            .Save(_binary);

        int asciiRows = 4_000;
        _ascii = Path.Combine(directory, "ascii.fits");
        FitsDocumentBuilder.Create()
            .AsciiTable(new AsciiTableBuilder()
                .AddInteger("id", Enumerable.Range(0, asciiRows).Select(i => (long)i).ToArray(), 8)
                .AddReal("value", Enumerable.Range(0, asciiRows).Select(i => i * 0.25).ToArray(), 12, 4, 'E'))
            .Build()
            .Save(_ascii);

        var heapRows = new int[1_000][];
        for (int i = 0; i < heapRows.Length; i++)
        {
            heapRows[i] = [i, i + 1, i + 2, i + 3];
        }

        _heap = Path.Combine(directory, "heap.fits");
        FitsDocumentBuilder.Create()
            .BinaryTable(new BinaryTableBuilder().AddVariable("samples", heapRows))
            .Build()
            .Save(_heap);

        _gzip = Path.Combine(directory, "gzip.fits");
        short[] gzipPixels = new short[512 * 512];
        for (int i = 0; i < gzipPixels.Length; i++)
        {
            gzipPixels[i] = (short)(i % 1000);
        }

        FitsDocumentBuilder.Create().GzipImage([512, 512], gzipPixels).Build().Save(_gzip);

        float[] tile = new float[256 * 256];
        for (int i = 0; i < tile.Length; i++)
        {
            tile[i] = i % 17 == 0 ? float.NaN : i * 0.01f;
        }

        _gzipShuffled = Path.Combine(directory, "gzip2.fits");
        FitsDocumentBuilder.Create().GzipImage([256, 256], tile, shuffle: true).Build().Save(_gzipShuffled);

        _checksum = Path.Combine(directory, "checksum.fits");
        FitsDocumentBuilder.Create().Image([128, 128], small.AsSpan(0, 128 * 128).ToArray()).Checksum().Build().Save(_checksum);

        _rice = Path.Combine(directory, "rice.fits");
        _hcompress = Path.Combine(directory, "hcomp.fits");
        WriteCompressedFixtures(root, _rice, _hcompress);

        string survey = Path.Combine(root, "SampleData", "Real", "sdss-spec-0266.fits");
        _sdss = File.Exists(survey) ? survey : null;

        var header = FitsHeader.Builder()
            .Integer("NAXIS", 2)
            .Text("CTYPE1", "RA---TAN")
            .Text("CTYPE2", "DEC--TAN")
            .Real("CRPIX1", 128)
            .Real("CRPIX2", 128)
            .Real("CRVAL1", 83.633)
            .Real("CRVAL2", -5.391)
            .Real("CDELT1", -0.01)
            .Real("CDELT2", 0.01)
            .Build();
        _world = FitsWorld.FromHeader(header);
    }

    [Benchmark(Description = "Open a 4×4 image")]
    public int OpenTiny()
    {
        using var document = FitsDocument.Open(_tiny);
        return document.Hdus.Count;
    }

    [Benchmark(Description = "Read a 256² int16 image")]
    public double ReadImage256() => SumImage(_image256);

    [Benchmark(Description = "Read a 1024² float32 image")]
    public double ReadImage1024() => SumImage(_image1024);

    [Benchmark(Description = "Scan 20,000 binary-table rows")]
    public double ScanBinaryColumn()
    {
        using var document = FitsDocument.Open(_binary);
        var column = document.Hdus[1].Table!.Column("flux");
        int rows = document.Hdus[1].Table!.RowCount;
        double sum = 0;
        for (int row = 0; row < rows; row++)
        {
            sum += column.ReadReal(row);
        }

        return sum;
    }

    [Benchmark(Description = "Read a 4,000-row ASCII table")]
    public double ReadAsciiTable()
    {
        using var document = FitsDocument.Open(_ascii);
        var column = document.Hdus[1].Table!.Column("value");
        double sum = 0;
        for (int row = 0; row < document.Hdus[1].Table!.RowCount; row++)
        {
            sum += column.ReadReal(row);
        }

        return sum;
    }

    [Benchmark(Description = "Read 1,000 variable-length rows")]
    public long ReadVariableColumn()
    {
        using var document = FitsDocument.Open(_heap);
        var column = document.Hdus[1].Table!.Column("samples");
        long sum = 0;
        for (int row = 0; row < document.Hdus[1].Table!.RowCount; row++)
        {
            for (int element = 0; element < column.Count(row); element++)
            {
                sum += column.ReadInteger(row, element);
            }
        }

        return sum;
    }

    [Benchmark(Description = "Write a 512² int16 image")]
    public int WriteImage512()
    {
        short[] pixels = new short[512 * 512];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (short)(i % 1000);
        }

        return FitsDocumentBuilder.Create().Image([512, 512], pixels).Build().ToArray().Length;
    }

    [Benchmark(Description = "Decompress a GZIP_1 image")]
    public double DecompressGzip() => SumImage(_gzip, 1);

    [Benchmark(Description = "Decompress a GZIP_2 image")]
    public double DecompressGzipShuffled() => SumImage(_gzipShuffled, 1);

    [Benchmark(Description = "Decompress a Rice image")]
    public double DecompressRice() => SumImage(_rice, 1);

    [Benchmark(Description = "Decompress an H-compress image")]
    public double DecompressHcompress() => SumImage(_hcompress, 1);

    [Benchmark(Description = "Open a checksummed image")]
    public bool VerifyChecksum()
    {
        using var document = FitsDocument.Open(_checksum, new FitsOpenOptions { VerifyChecksum = true });
        return document.Hdus[0].Integrity.IsValid;
    }

    [Benchmark(Description = "TAN coordinates, 100,000 pixels")]
    public double EvaluateWcs()
    {
        double sum = 0;
        for (int i = 0; i < 100_000; i++)
        {
            _pixel[0] = (i % 400) + 1;
            _pixel[1] = (i / 400) + 1;
            _world.PixelToWorld(_pixel, _worldPixel);
            sum += _worldPixel[0] + _worldPixel[1];
        }

        return sum;
    }

    [Benchmark(Description = "Read an SDSS spectrum column")]
    public double ReadSdssSpectrum()
    {
        if (_sdss is null)
        {
            return 0;
        }

        using var document = FitsDocument.Open(_sdss);
        var column = document.Hdus[1].Table!.Column("flux");
        double sum = 0;
        for (int row = 0; row < document.Hdus[1].Table!.RowCount; row++)
        {
            sum += column.ReadReal(row);
        }

        return sum;
    }

    private static double SumImage(string path, int hdu = 0)
    {
        using var document = FitsDocument.Open(path);
        double sum = document.Hdus[hdu].Image!.SumPhysical();

        return sum;
    }

    private static void WriteCompressedFixtures(string root, string rice, string hcompress)
    {
        string python = PythonRuntime.Find(root);

        string script = Path.Combine(Path.GetDirectoryName(rice)!, "compress_bench.py");
        string ricePath = rice.Replace("\\", "/");
        string hcompressPath = hcompress.Replace("\\", "/");
        string scriptText =
            "import numpy as np\n" +
            "from astropy.io import fits\n" +
            "data = (np.arange(128 * 128, dtype=np.int16).reshape(128, 128) % 500) - 100\n" +
            "fits.CompImageHDU(data=data, compression_type='RICE_1').writeto(r'" + ricePath + "', overwrite=True)\n" +
            "fits.CompImageHDU(data=data, compression_type='HCOMPRESS_1').writeto(r'" + hcompressPath + "', overwrite=True)\n";
        File.WriteAllText(script, scriptText);
        var start = new System.Diagnostics.ProcessStartInfo(python)
        {
            WorkingDirectory = root,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(script);
        using var process = System.Diagnostics.Process.Start(start)!;
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(error);
        }
    }

    internal static string FindRoot()
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

    internal static string Label(string method) => method switch
    {
        nameof(OpenTiny) => "Open a 4×4 image",
        nameof(ReadImage256) => "Read a 256² int16 image",
        nameof(ReadImage1024) => "Read a 1024² float32 image",
        nameof(ScanBinaryColumn) => "Scan 20,000 binary-table rows",
        nameof(ReadAsciiTable) => "Read a 4,000-row ASCII table",
        nameof(ReadVariableColumn) => "Read 1,000 variable-length rows",
        nameof(WriteImage512) => "Write a 512² int16 image",
        nameof(DecompressGzip) => "Decompress a GZIP_1 image",
        nameof(DecompressGzipShuffled) => "Decompress a GZIP_2 image",
        nameof(DecompressRice) => "Decompress a Rice image",
        nameof(DecompressHcompress) => "Decompress an H-compress image",
        nameof(VerifyChecksum) => "Open a checksummed image",
        nameof(EvaluateWcs) => "TAN coordinates, 100,000 pixels",
        nameof(ReadSdssSpectrum) => "Read an SDSS spectrum column",
        _ => method,
    };
}
