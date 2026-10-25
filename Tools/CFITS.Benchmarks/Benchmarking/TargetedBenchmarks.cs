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
    private static readonly byte[] ChecksumPayload = Enumerable.Range(0, 1_048_576).Select(index => (byte)index).ToArray();
    private static readonly FitsWorld BenchmarkWorld = CreateWorld();
    private static readonly FitsClock BenchmarkClock = FitsClock.FromHeader(
        FitsHeader.Builder().Text("TIMESYS", "TT").Text("TIMEUNIT", "s").Real("MJDREF", 50000).Build());

    public static void Run()
    {
        string root = FitsScenarios.FindRoot();
        string data = Path.Combine(root, "Benchmarks", "Data");
        string results = Path.Combine(root, "Benchmarks", "Results", "optimization-targeted.csv");
        var scenarios = CreateScenarios(data)
            .Where(scenario => scenario.Family is "wcs" or "clock" or "checksum" or "micro-arithmetic")
            .ToArray();

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
        string checksum = Path.Combine(data, "checksum.fits");

        return
        [
            new("pixel_sum", "pixels", () => SumImage(image256)),
            new("pixel_bulk_copy", "pixels", () => CopyImage(image256)),
            new("pixel_stored_copy", "pixels", () => CopyStoredImage(image256)),
            new("pixel_shuffle_decode", "pixels", () => SumImage(gzipShuffled, 1)),
            new("pixel_offset_multiply", "micro-arithmetic", PixelOffsetMultiply),
            new("pixel_offset_counter", "micro-arithmetic", PixelOffsetCounter),
            new("gzip_decode", "compression", () => SumImage(gzip, 1)),
            new("tile_index_division", "micro-arithmetic", TileIndexDivision),
            new("tile_index_counter", "micro-arithmetic", TileIndexCounter),
            new("rice_decode", "compression", () => SumImage(rice, 1)),
            new("hcompress_decode", "compression", () => SumImage(hcompress, 1)),
            new("checksum_verify", "checksum", () => VerifyChecksum(checksum)),
            new("checksum_split_division", "micro-arithmetic", ChecksumSplitDivision),
            new("checksum_split_bitwise", "micro-arithmetic", ChecksumSplitBitwise),
            new("table_bit_packing", "tables", PackBits),
            new("table_ascii_writer", "tables", WriteAsciiTable),
            new("table_direct_writer", "tables", WriteBinaryTable),
            new("parse_open", "parse-open-header", () => Open(tiny)),
            new("header_lookup", "parse-open-header", () => HeaderLookup(tiny)),
            new("wcs_projection", "wcs", () => _sink = EvaluateWcs()),
            new("wcs_inverse_allocating", "wcs", () => { _sink = EvaluateWcsInverse(); }),
            new("wcs_inverse_span", "wcs", () => { _sink = EvaluateWcsInverseSpan(); }),
            new("clock_unit_cached", "clock", () => { _sink = EvaluateClock(); }),
            new("checksum_sum_1mb", "checksum", () => { _sink = FitsChecksum.Sum(ChecksumPayload); }),
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

    private static double EvaluateWcsInverse()
    {
        double sum = 0;
        for (int index = 0; index < 100_000; index++)
        {
            double[] pixel = BenchmarkWorld.WorldToPixel([83.633 + (index * 1e-5), -5.391]);
            sum += pixel[0] + pixel[1];
        }

        return sum;
    }

    private static double EvaluateWcsInverseSpan()
    {
        Span<double> pixel = stackalloc double[2];
        double sum = 0;
        for (int index = 0; index < 100_000; index++)
        {
            BenchmarkWorld.WorldToPixel([83.633 + (index * 1e-5), -5.391], pixel);
            sum += pixel[0] + pixel[1];
        }

        return sum;
    }

    private static double EvaluateClock()
    {
        double sum = 0;
        for (int index = 0; index < 100_000; index++)
        {
            sum += BenchmarkClock.ToModifiedJulianDate(index);
        }

        return sum;
    }

    private static FitsWorld CreateWorld() =>
        FitsWorld.FromHeader(FitsHeader.Builder()
            .Integer("WCSAXES", 2)
            .Text("CTYPE1", "RA---TAN")
            .Text("CTYPE2", "DEC--TAN")
            .Real("CRPIX1", 128)
            .Real("CRPIX2", 128)
            .Real("CRVAL1", 83.633)
            .Real("CRVAL2", -5.391)
            .Real("CDELT1", -0.01)
            .Real("CDELT2", 0.01)
            .Build());

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

    private static void CopyImage(string path)
    {
        using var document = FitsDocument.Open(path);
        FitsImage image = document.Hdus[0].Image!;
        var destination = new double[image.Length];
        image.CopyPhysical(destination);
        _sink = destination[^1];
    }

    private static void CopyStoredImage(string path)
    {
        using var document = FitsDocument.Open(path);
        FitsImage image = document.Hdus[0].Image!;
        var destination = new short[image.Length];
        image.CopyInteger(destination);
        _sink = destination[^1];
    }

    private static void PixelOffsetMultiply()
    {
        long total = 0;
        for (int index = 0; index < 1_000_000; index++)
        {
            total += index * sizeof(short);
        }

        _sink = total;
    }

    private static void PixelOffsetCounter()
    {
        long total = 0;
        int offset = 0;
        for (int index = 0; index < 1_000_000; index++)
        {
            total += offset;
            offset += sizeof(short);
        }

        _sink = total;
    }

    private static void TileIndexDivision()
    {
        long total = 0;
        int[] axes = [17, 13, 7];
        int[] span = [5, 4, 3];
        int tilePixels = span[0] * span[1] * span[2];
        for (int repeat = 0; repeat < 10_000; repeat++)
        {
            for (int tileIndex = 0; tileIndex < tilePixels; tileIndex++)
            {
                int remaining = tileIndex;
                int address = 0;
                int stride = 1;
                for (int axis = 0; axis < axes.Length; axis++)
                {
                    int coordinate = remaining % span[axis];
                    remaining /= span[axis];
                    address += coordinate * stride;
                    stride *= axes[axis];
                }

                total += address;
            }
        }

        _sink = total;
    }

    private static void TileIndexCounter()
    {
        long total = 0;
        int[] axes = [17, 13, 7];
        int[] span = [5, 4, 3];
        int tilePixels = span[0] * span[1] * span[2];
        int address = 0;
        int coordinateStride;
        Span<int> coordinates = stackalloc int[3];
        for (int repeat = 0; repeat < 10_000; repeat++)
        {
            address = 0;
            coordinates.Clear();
            for (int tileIndex = 0; tileIndex < tilePixels; tileIndex++)
            {
                total += address;
                coordinateStride = 1;
                for (int axis = 0; axis < axes.Length; axis++)
                {
                    coordinates[axis]++;
                    address += coordinateStride;
                    if (coordinates[axis] < span[axis])
                    {
                        break;
                    }

                    coordinates[axis] = 0;
                    address -= span[axis] * coordinateStride;
                    coordinateStride *= axes[axis];
                }
            }
        }

        _sink = total;
    }

    private static void ChecksumSplitDivision()
    {
        int total = 0;
        for (int repeat = 0; repeat < 1_000_000; repeat++)
        {
            int piece = repeat & 0xFF;
            total += piece / 4;
            total += piece % 4;
        }

        _sink = total;
    }

    private static void ChecksumSplitBitwise()
    {
        int total = 0;
        for (int repeat = 0; repeat < 1_000_000; repeat++)
        {
            int piece = repeat & 0xFF;
            total += piece >> 2;
            total += piece & 3;
        }

        _sink = total;
    }

    private static double SumImage(string path, int hdu = 0)
    {
        using var document = FitsDocument.Open(path);
        return document.Hdus[hdu].Image!.SumPhysical();
    }

    private static bool VerifyChecksum(string path)
    {
        using var document = FitsDocument.Open(path, new FitsOpenOptions { VerifyChecksum = true });
        return document.Hdus[0].Integrity.IsValid;
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
