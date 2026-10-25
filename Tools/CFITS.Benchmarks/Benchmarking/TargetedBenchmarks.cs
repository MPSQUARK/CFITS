using System.Diagnostics;
using System.Globalization;

namespace CFITS.Benchmarks;

/// <summary>
/// Measures representative CFITS-only operations without invoking the comparison suite.
/// </summary>
internal static class TargetedBenchmarks
{
    private const int Warmups = 3;
    private const int Iterations = 8;
    private static object? _sink;

    public static void Run()
    {
        string root = FitsScenarios.FindRoot();
        string data = Path.Combine(root, "Benchmarks", "Data");
        string results = Path.Combine(root, "Benchmarks", "Results", "optimization-targeted.csv");
        var scenarios = CreateScenarios(data);

        Directory.CreateDirectory(Path.GetDirectoryName(results)!);
        using var csv = new StreamWriter(results);
        csv.WriteLine("scenario,family,warmups,iterations,mean_ns,stddev_ns,allocated_bytes,stddev_allocated_bytes,status,notes");
        foreach (TargetedScenario scenario in scenarios)
        {
            TargetedSample sample = Measure(scenario.Action);
            csv.WriteLine(string.Join(",",
                scenario.Name,
                scenario.Family,
                Warmups.ToString(CultureInfo.InvariantCulture),
                Iterations.ToString(CultureInfo.InvariantCulture),
                Format(sample.MeanNanoseconds),
                Format(sample.StandardDeviationNanoseconds),
                Format(sample.MeanAllocatedBytes),
                Format(sample.StandardDeviationAllocatedBytes),
                "available",
                "CFITS-only representative measurement"));
        }

        Console.WriteLine($"Targeted results: {results}");
    }

    private static IReadOnlyList<TargetedScenario> CreateScenarios(string data)
    {
        string tiny = Path.Combine(data, "tiny.fits");
        string image256 = Path.Combine(data, "image256.fits");
        string ascii = Path.Combine(data, "ascii.fits");
        string gzip = Path.Combine(data, "gzip.fits");
        string gzipShuffled = Path.Combine(data, "gzip2.fits");
        string rice = Path.Combine(data, "rice.fits");
        string hcompress = Path.Combine(data, "hcomp.fits");

        return
        [
            new("pixel_sum", "pixels", () => SumImage(image256)),
            new("pixel_shuffle_decode", "pixels", () => SumImage(gzipShuffled, 1)),
            new("gzip_decode", "compression", () => SumImage(gzip, 1)),
            new("rice_decode", "compression", () => SumImage(rice, 1)),
            new("hcompress_decode", "compression", () => SumImage(hcompress, 1)),
            new("table_bit_packing", "tables", PackBits),
            new("table_ascii_writer", "tables", WriteAsciiTable),
            new("table_direct_writer", "tables", WriteBinaryTable),
            new("parse_open", "parse-open-header", () => Open(tiny)),
            new("header_lookup", "parse-open-header", () => HeaderLookup(tiny)),
            new("wcs_projection", "wcs", () => _sink = EvaluateWcs()),
            new("write_allocation", "write-read-allocations", () => _sink = WriteImage()),
            new("read_allocation", "write-read-allocations", () => ReadImage(image256)),
        ];
    }

    private static TargetedSample Measure(Action action)
    {
        for (int index = 0; index < Warmups; index++)
        {
            action();
        }

        var timings = new double[Iterations];
        var allocations = new double[Iterations];
        for (int index = 0; index < Iterations; index++)
        {
            var stopwatch = Stopwatch.StartNew();
            action();
            stopwatch.Stop();
            timings[index] = stopwatch.Elapsed.TotalNanoseconds;

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long before = GC.GetAllocatedBytesForCurrentThread();
            action();
            allocations[index] = GC.GetAllocatedBytesForCurrentThread() - before;
        }

        return new TargetedSample(
            Mean(timings),
            StandardDeviation(timings),
            Mean(allocations),
            StandardDeviation(allocations));
    }

    private static void PackBits()
    {
        var rows = new bool[256][];
        for (int row = 0; row < rows.Length; row++)
        {
            rows[row] = new bool[64];
            for (int bit = 0; bit < rows[row].Length; bit++)
            {
                rows[row][bit] = ((row + bit) & 3) == 0;
            }
        }

        _sink = FitsDocumentBuilder.Create()
            .BinaryTable(new BinaryTableBuilder().AddBits("flags", 64, rows))
            .Build()
            .ToArray();
    }

    private static void WriteAsciiTable()
    {
        const int rows = 1_000;
        _sink = FitsDocumentBuilder.Create()
            .AsciiTable(new AsciiTableBuilder()
                .AddInteger("id", Enumerable.Range(0, rows).Select(i => (long)i).ToArray(), 8)
                .AddReal("value", Enumerable.Range(0, rows).Select(i => i * 0.25).ToArray(), 12, 4, 'E'))
            .Build()
            .ToArray();
    }

    private static void WriteBinaryTable()
    {
        const int rows = 1_000;
        _sink = FitsDocumentBuilder.Create()
            .BinaryTable(new BinaryTableBuilder()
                .Add("flux", Enumerable.Range(0, rows).Select(i => i * 0.25).ToArray())
                .Add("flags", Enumerable.Range(0, rows).Select(i => i % 7).ToArray()))
            .Build()
            .ToArray();
    }

    private static int Open(string path)
    {
        using var document = FitsDocument.Open(path);
        return document.Hdus.Count;
    }

    private static long HeaderLookup(string path)
    {
        using var document = FitsDocument.Open(path);
        return document.Hdus[0].Header.GetInt64("NAXIS1");
    }

    private static double EvaluateWcs()
    {
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
        FitsWorld world = FitsWorld.FromHeader(header);
        Span<double> pixel = stackalloc double[2];
        Span<double> coordinates = stackalloc double[2];
        double sum = 0;
        for (int index = 0; index < 100_000; index++)
        {
            pixel[0] = (index % 400) + 1;
            pixel[1] = (index / 400) + 1;
            world.PixelToWorld(pixel, coordinates);
            sum += coordinates[0] + coordinates[1];
        }

        return sum;
    }

    private static int WriteImage()
    {
        short[] pixels = new short[256 * 256];
        for (int index = 0; index < pixels.Length; index++)
        {
            pixels[index] = (short)(index % 1000);
        }

        return FitsDocumentBuilder.Create().Image([256, 256], pixels).Build().ToArray().Length;
    }

    private static double ReadImage(string path)
    {
        using var document = FitsDocument.Open(path);
        return document.Hdus[0].Image!.SumPhysical();
    }

    private static double SumImage(string path, int hdu = 0)
    {
        using var document = FitsDocument.Open(path);
        return document.Hdus[hdu].Image!.SumPhysical();
    }

    private static double Mean(double[] values) => values.Average();

    private static double StandardDeviation(double[] values)
    {
        double mean = Mean(values);
        double variance = values.Sum(value => (value - mean) * (value - mean)) / (values.Length - 1);
        return Math.Sqrt(variance);
    }

    private static string Format(double value) => value.ToString("G17", CultureInfo.InvariantCulture);

}
