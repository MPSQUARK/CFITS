using System.Diagnostics;
using System.Globalization;
using nom.tam.fits;
using ScottPlot;

namespace CFITS.Benchmarks;

/// <summary>
/// Times the same files with CFITS, Astropy, and CSharpFITS.
/// Each iteration repeats the operation until at least 100 ms have elapsed, matching a short BenchmarkDotNet iteration.
/// </summary>
internal static class Comparison
{
    private const int Warmup = 2;
    private const int Iterations = 6;
    private static readonly TimeSpan MinimumIteration = TimeSpan.FromMilliseconds(100);

    public static void Probe()
    {
        try
        {
            var rows = new short[4][];
            for (int y = 0; y < rows.Length; y++)
            {
                rows[y] = new short[4];
            }

            var fits = new Fits();
            fits.AddHDU(Fits.MakeHDU(rows));
            using var stream = new MemoryStream();
            fits.Write(stream);
            Console.WriteLine($"wrote {stream.Length}");
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
        }
    }

    public static void Run()
    {
        string root = FitsScenarios.FindRoot();
        string directory = Path.Combine(root, "benchmarks", "data");
        string sdss = Path.Combine(root, "Samples", "real", "sdss-spec-0266.fits");
        var files = new Dictionary<string, string>
        {
            ["tiny"] = Path.Combine(directory, "tiny.fits"),
            ["image256"] = Path.Combine(directory, "image256.fits"),
            ["image1024"] = Path.Combine(directory, "image1024.fits"),
            ["binary"] = Path.Combine(directory, "bintable.fits"),
            ["ascii"] = Path.Combine(directory, "ascii.fits"),
            ["heap"] = Path.Combine(directory, "heap.fits"),
            ["gzip"] = Path.Combine(directory, "gzip.fits"),
            ["gzip2"] = Path.Combine(directory, "gzip2.fits"),
            ["checksum"] = Path.Combine(directory, "checksum.fits"),
            ["rice"] = Path.Combine(directory, "rice.fits"),
            ["hcomp"] = Path.Combine(directory, "hcomp.fits"),
            ["sdss"] = sdss,
        };

        var world = TanWorld();
        var scenarios = new (string Label, Action Cfits, Action? Sharp, string Astropy)[]
        {
            ("Open a 4×4 image", () => CfitsOpen(files["tiny"]), () => SharpOpen(files["tiny"]), "open_tiny"),
            ("Read a 256² int16 image", () => CfitsSum(files["image256"]), () => SharpSum(files["image256"]), "read_image256"),
            ("Read a 1024² float32 image", () => CfitsSum(files["image1024"]), () => SharpSum(files["image1024"]), "read_image1024"),
            ("Scan 20,000 binary-table rows", () => CfitsColumn(files["binary"], "flux"), () => SharpColumn(files["binary"], "flux"), "scan_binary"),
            ("Read a 4,000-row ASCII table", () => CfitsColumn(files["ascii"], "value"), () => SharpColumn(files["ascii"], "value"), "read_ascii"),
            ("Read 1,000 variable-length rows", () => CfitsVariable(files["heap"]), () => SharpVariable(files["heap"]), "read_heap"),
            ("Write a 512² int16 image", CfitsWrite, SharpWrite, "write_image"),
            ("Decompress a GZIP_1 image", () => CfitsSum(files["gzip"], 1), null, "decompress_gzip"),
            ("Decompress a GZIP_2 image", () => CfitsSum(files["gzip2"], 1), null, "decompress_gzip2"),
            ("Decompress a Rice image", () => CfitsSum(files["rice"], 1), null, "decompress_rice"),
            ("Decompress an H-compress image", () => CfitsSum(files["hcomp"], 1), null, "decompress_hcomp"),
            ("Open a checksummed image", () => CfitsChecksum(files["checksum"]), null, "open_checksum"),
            ("TAN coordinates, 100,000 pixels", () => CfitsWcs(world), null, "tan_wcs"),
            ("Read an SDSS spectrum column", () => CfitsColumn(files["sdss"], "flux"), () => SharpColumn(files["sdss"], "flux"), "read_sdss"),
        };

        Console.WriteLine("Preparing the shared FITS files...");
        new FitsScenarios().Prepare();
        Console.WriteLine("Measuring CFITS and CSharpFITS...");
        var measured = new List<Row>();
        foreach (var scenario in scenarios)
        {
            if (scenario.Label.Contains("SDSS", StringComparison.Ordinal) && !File.Exists(sdss))
            {
                continue;
            }

            Console.WriteLine(scenario.Label);
            var cfits = Measure(scenario.Cfits);
            Sample? sharp = scenario.Sharp is null ? null : MeasureSafe(scenario.Sharp);
            measured.Add(new Row(scenario.Label, scenario.Astropy, cfits, sharp, null));
        }

        Console.WriteLine("Measuring Astropy...");
        string astropyCsv = Path.Combine(root, "benchmarks", "astropy-scenarios.csv");
        RunAstropy(root, directory, sdss, astropyCsv);
        var astropy = ReadAstropy(astropyCsv);
        for (int index = 0; index < measured.Count; index++)
        {
            var row = measured[index];
            measured[index] = row with { Astropy = astropy.GetValueOrDefault(row.Key) };
        }

        string png = Path.Combine(root, "benchmarks", "fits-comparison.png");
        string csv = Path.Combine(root, "benchmarks", "fits-comparison.csv");
        Save(measured, png, csv);
        Console.WriteLine($"Plot: {png}");
        Console.WriteLine($"Table: {csv}");
    }

    private static Sample Measure(Action action)
    {
        for (int i = 0; i < Warmup; i++)
        {
            action();
        }

        var samples = new double[Iterations];
        var allocations = new double[Iterations];
        for (int i = 0; i < Iterations; i++)
        {
            int count = 0;
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < MinimumIteration || count == 0)
            {
                action();
                count++;
            }

            samples[i] = watch.Elapsed.TotalMilliseconds / count;
            allocations[i] = MeasureAllocation(action);
        }

        return new Sample(
            MeanMilliseconds: Mean(samples),
            StdDevMilliseconds: StandardDeviation(samples),
            MeanAllocatedBytes: Mean(allocations),
            StdDevAllocatedBytes: StandardDeviation(allocations));
    }

    private static double MeasureAllocation(Action action)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static double Mean(double[] values) => values.Average();

    private static double StandardDeviation(double[] values)
    {
        double mean = Mean(values);
        double variance = values.Sum(value => (value - mean) * (value - mean)) / (values.Length - 1);
        return Math.Sqrt(variance);
    }

    private static Sample? MeasureSafe(Action action)
    {
        TextWriter output = Console.Out;
        TextWriter error = Console.Error;
        Console.SetOut(TextWriter.Null);
        Console.SetError(TextWriter.Null);
        try
        {
            return Measure(action);
        }
        catch (Exception exception)
        {
            Console.SetOut(output);
            Console.SetError(error);
            Console.WriteLine($"  CSharpFITS skipped: {exception.GetType().Name}: {exception.Message}");
            return null;
        }
        finally
        {
            Console.SetOut(output);
            Console.SetError(error);
        }
    }

    private static void CfitsOpen(string path)
    {
        using var document = FitsDocument.Open(path);
        _ = document.Hdus.Count;
    }

    private static void CfitsSum(string path, int hdu = 0)
    {
        using var document = FitsDocument.Open(path);
        double sum = 0;
        foreach (double value in document.Hdus[hdu].Image!.ReadPhysical())
        {
            if (!double.IsNaN(value))
            {
                sum += value;
            }
        }

        _ = sum;
    }

    private static void CfitsColumn(string path, string name)
    {
        using var document = FitsDocument.Open(path);
        var table = document.Hdus[1].Table!;
        var column = table.Column(name);
        double sum = 0;
        for (int row = 0; row < table.RowCount; row++)
        {
            sum += column.ReadReal(row);
        }

        _ = sum;
    }

    private static void CfitsVariable(string path)
    {
        using var document = FitsDocument.Open(path);
        var table = document.Hdus[1].Table!;
        var column = table.Column("samples");
        long sum = 0;
        for (int row = 0; row < table.RowCount; row++)
        {
            sum += column.ReadInteger(row, 0);
        }

        _ = sum;
    }

    private static void CfitsWrite()
    {
        short[] pixels = new short[512 * 512];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (short)(i % 1000);
        }

        _ = FitsDocumentBuilder.Create().Image([512, 512], pixels).Build().ToArray().Length;
    }

    private static void CfitsChecksum(string path)
    {
        using var document = FitsDocument.Open(path, new FitsOpenOptions { VerifyChecksum = true });
        _ = document.Hdus[0].Integrity.IsValid;
    }

    private static void CfitsWcs(FitsWorld world)
    {
        var pixel = new double[2];
        double sum = 0;
        for (int i = 0; i < 100_000; i++)
        {
            pixel[0] = (i % 400) + 1;
            pixel[1] = (i / 400) + 1;
            var coordinates = world.PixelToWorld(pixel);
            sum += coordinates[0] + coordinates[1];
        }

        _ = sum;
    }

    private static FitsWorld TanWorld()
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
        return FitsWorld.FromHeader(header);
    }

    private static void SharpOpen(string path)
    {
        var fits = new Fits(path);
        BasicHDU[] hdus = fits.Read();
        _ = hdus.Length;
        fits.Close();
    }

    private static void SharpSum(string path, int hdu = 0)
    {
        var fits = new Fits(path);
        var image = (ImageHDU)fits.Read()[hdu];
        _ = SumArray((Array)image.Kernel);
        fits.Close();
    }

    private static void SharpColumn(string path, string name)
    {
        var fits = new Fits(path);
        var table = (TableHDU)fits.Read()[1];
        _ = SumArray((Array)table.GetColumn(name));
        fits.Close();
    }

    private static void SharpVariable(string path)
    {
        var fits = new Fits(path);
        var table = (BinaryTableHDU)fits.Read()[1];
        var column = (Array)table.GetColumn("samples");
        long sum = 0;
        for (int row = 0; row < column.Length; row++)
        {
            var values = (Array)column.GetValue(row)!;
            sum += Convert.ToInt64(values.GetValue(0), CultureInfo.InvariantCulture);
        }

        _ = sum;
        fits.Close();
    }

    private static void SharpWrite()
    {
        var rows = new short[512][];
        for (int y = 0; y < rows.Length; y++)
        {
            var row = new short[512];
            for (int x = 0; x < row.Length; x++)
            {
                row[x] = (short)(((y * 512) + x) % 1000);
            }

            rows[y] = row;
        }

        var fits = new Fits();
        fits.AddHDU(Fits.MakeHDU(rows));
        var stream = new UndisposableStream();
        fits.Write(stream);
        _ = stream.Length;
        fits.Close();
    }

    private sealed class UndisposableStream : MemoryStream
    {
        protected override void Dispose(bool disposing)
        {
        }
    }

    private static double SumArray(Array values)
    {
        double sum = 0;
        foreach (object value in values)
        {
            if (value is Array nested)
            {
                sum += SumArray(nested);
            }
            else if (value is not null)
            {
                double number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (!double.IsNaN(number))
                {
                    sum += number;
                }
            }
        }

        return sum;
    }

    private static void RunAstropy(string root, string directory, string sdss, string csvPath)
    {
        string python = Path.Combine(root, ".venv", "Scripts", "python.exe");
        string script = Path.Combine(root, "benchmarks", "compare_astropy.py");
        var start = new ProcessStartInfo(python, $"\"{script}\" \"{directory}\" \"{sdss}\" \"{csvPath}\"")
        {
            WorkingDirectory = root,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(start)!;
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(error);
        }
    }

    private static Dictionary<string, Sample> ReadAstropy(string path)
    {
        var samples = new Dictionary<string, Sample>(StringComparer.Ordinal);
        foreach (string line in File.ReadAllLines(path).Skip(1))
        {
            string[] parts = line.Split(',');
            if (parts.Length < 5 || parts[1] == "unsupported")
            {
                continue;
            }

            samples[parts[0]] = new Sample(
                double.Parse(parts[1], CultureInfo.InvariantCulture),
                double.Parse(parts[2], CultureInfo.InvariantCulture),
                double.Parse(parts[3], CultureInfo.InvariantCulture),
                double.Parse(parts[4], CultureInfo.InvariantCulture));
        }

        return samples;
    }

    private static void Save(IReadOnlyList<Row> rows, string pngPath, string csvPath)
    {
        var plot = new Plot();
        plot.FigureBackground.Color = Colors.White;
        plot.DataBackground.Color = Colors.White;
        plot.Axes.Color(Color.FromHex("#7b8da6"));
        plot.Grid.MajorLineColor = Color.FromHex("#d8e0ec").WithAlpha(0.8);
        var series = new (string Name, Color Color, Func<Row, Sample?> Value)[]
        {
            ("CFITS", Color.FromHex("#0d8f8a"), row => row.Cfits),
            ("Astropy", Color.FromHex("#1e4d8c"), row => row.Astropy),
            ("CSharpFITS", Color.FromHex("#e0634a"), row => row.Sharp),
        };

        var ordered = rows.OrderBy(row => row.Cfits.MeanMilliseconds).ToArray();
        for (int seriesIndex = 0; seriesIndex < series.Length; seriesIndex++)
        {
            var bars = new List<Bar>();
            for (int rowIndex = 0; rowIndex < ordered.Length; rowIndex++)
            {
                Sample? sample = series[seriesIndex].Value(ordered[rowIndex]);
                if (sample is null)
                {
                    continue;
                }

                bars.Add(new Bar
                {
                    Position = rowIndex + ((seriesIndex - 1) * 0.26),
                    Value = sample.Value.MeanMilliseconds,
                    Error = sample.Value.StdDevMilliseconds,
                    Size = 0.24,
                    ErrorSize = 0.08,
                });
            }

            if (bars.Count == 0)
            {
                continue;
            }

            var plotted = plot.Add.Bars(bars.ToArray());
            plotted.Horizontal = true;
            plotted.Color = series[seriesIndex].Color;
            plotted.LegendText = series[seriesIndex].Name;
        }

        double[] positions = Enumerable.Range(0, ordered.Length).Select(index => (double)index).ToArray();
        plot.Axes.Left.SetTicks(positions, ordered.Select(row => row.Label).ToArray());
        plot.Axes.Left.TickLabelStyle.FontSize = 12;
        plot.Axes.Bottom.Label.Text = "Mean time (ms)";
        plot.Axes.Title.Label.Text = "Same scenarios: CFITS, Astropy, and CSharpFITS";
        plot.Axes.Title.Label.FontSize = 18;
        plot.Axes.Margins(right: 0.12);
        double widest = ordered.Max(row => new[] { row.Cfits, row.Astropy, row.Sharp }.Where(sample => sample is not null).Max(sample => sample!.Value.MeanMilliseconds + sample.Value.StdDevMilliseconds));
        plot.Axes.SetLimitsX(0, widest * 1.08);
        plot.ShowLegend(Alignment.LowerRight);
        Directory.CreateDirectory(Path.GetDirectoryName(pngPath)!);
        plot.SavePng(pngPath, 1200, 860);

        using var csv = new StreamWriter(csvPath);
        csv.WriteLine("scenario,cfits_mean_ms,cfits_stddev_ms,cfits_managed_allocated_bytes,cfits_managed_allocated_stddev_bytes,astropy_mean_ms,astropy_stddev_ms,astropy_python_peak_bytes,astropy_python_peak_stddev_bytes,csharpfits_mean_ms,csharpfits_stddev_ms,csharpfits_managed_allocated_bytes,csharpfits_managed_allocated_stddev_bytes");
        foreach (var row in ordered)
        {
            csv.Write(row.Label);
            WriteSample(csv, row.Cfits);
            WriteSample(csv, row.Astropy);
            WriteSample(csv, row.Sharp);
            csv.WriteLine();
        }

        string memoryPngPath = Path.Combine(Path.GetDirectoryName(pngPath)!, "fits-comparison-memory.png");
        string memoryCsvPath = Path.Combine(Path.GetDirectoryName(csvPath)!, "fits-comparison-memory.csv");
        SaveMemory(rows, memoryPngPath, memoryCsvPath);
        Console.WriteLine($"Memory plot: {memoryPngPath}");
        Console.WriteLine($"Memory table: {memoryCsvPath}");
    }

    private static void WriteSample(StreamWriter csv, Sample? sample)
    {
        if (sample is null)
        {
            csv.Write(",,");
            return;
        }

        csv.Write(',');
        csv.Write(sample.Value.MeanMilliseconds.ToString("G17", CultureInfo.InvariantCulture));
        csv.Write(',');
        csv.Write(sample.Value.StdDevMilliseconds.ToString("G17", CultureInfo.InvariantCulture));
        csv.Write(',');
        csv.Write(sample.Value.MeanAllocatedBytes.ToString("G17", CultureInfo.InvariantCulture));
        csv.Write(',');
        csv.Write(sample.Value.StdDevAllocatedBytes.ToString("G17", CultureInfo.InvariantCulture));
    }

    private static void SaveMemory(IReadOnlyList<Row> rows, string pngPath, string csvPath)
    {
        var plot = new Plot();
        plot.FigureBackground.Color = Colors.White;
        plot.DataBackground.Color = Colors.White;
        plot.Axes.Color(Color.FromHex("#7b8da6"));
        plot.Grid.MajorLineColor = Color.FromHex("#d8e0ec").WithAlpha(0.8);

        var series = new (string Name, Color Color, Func<Row, Sample?> Value)[]
        {
            ("CFITS", Color.FromHex("#0d8f8a"), row => row.Cfits),
            ("Astropy (Python peak)", Color.FromHex("#1e4d8c"), row => row.Astropy),
            ("CSharpFITS", Color.FromHex("#e0634a"), row => row.Sharp),
        };
        var ordered = rows.OrderBy(row => row.Cfits.MeanAllocatedBytes).ToArray();
        for (int seriesIndex = 0; seriesIndex < series.Length; seriesIndex++)
        {
            var bars = new List<Bar>();
            for (int rowIndex = 0; rowIndex < ordered.Length; rowIndex++)
            {
                Sample? sample = series[seriesIndex].Value(ordered[rowIndex]);
                if (sample is null)
                {
                    continue;
                }

                bars.Add(new Bar
                {
                    Position = rowIndex + ((seriesIndex - 1) * 0.26),
                    Value = sample.Value.MeanAllocatedBytes / (1024 * 1024),
                    Error = sample.Value.StdDevAllocatedBytes / (1024 * 1024),
                    Size = 0.24,
                    ErrorSize = 0.08,
                });
            }

            if (bars.Count == 0)
            {
                continue;
            }

            var plotted = plot.Add.Bars(bars.ToArray());
            plotted.Horizontal = true;
            plotted.Color = series[seriesIndex].Color;
            plotted.LegendText = series[seriesIndex].Name;
        }

        double[] positions = Enumerable.Range(0, ordered.Length).Select(index => (double)index).ToArray();
        plot.Axes.Left.SetTicks(positions, ordered.Select(row => row.Label).ToArray());
        plot.Axes.Left.TickLabelStyle.FontSize = 12;
        plot.Axes.Bottom.Label.Text = "Mean allocated memory (MiB)";
        plot.Axes.Title.Label.Text = "Same scenarios: allocated memory";
        plot.Axes.Title.Label.FontSize = 18;
        plot.Axes.Margins(right: 0.12);
        double widest = ordered.Max(row => new[] { row.Cfits, row.Astropy, row.Sharp }
            .Where(sample => sample is not null)
            .Max(sample => (sample!.Value.MeanAllocatedBytes + sample.Value.StdDevAllocatedBytes) / (1024 * 1024)));
        plot.Axes.SetLimitsX(0, widest * 1.08);
        plot.ShowLegend(Alignment.LowerRight);
        plot.SavePng(pngPath, 1200, 860);

        using var csv = new StreamWriter(csvPath);
        csv.WriteLine("scenario,cfits_managed_allocated_bytes,cfits_managed_allocated_stddev_bytes,astropy_python_peak_bytes,astropy_python_peak_stddev_bytes,csharpfits_managed_allocated_bytes,csharpfits_managed_allocated_stddev_bytes");
        foreach (var row in ordered)
        {
            csv.Write(row.Label);
            WriteAllocation(csv, row.Cfits);
            WriteAllocation(csv, row.Astropy);
            WriteAllocation(csv, row.Sharp);
            csv.WriteLine();
        }
    }

    private static void WriteAllocation(StreamWriter csv, Sample? sample)
    {
        if (sample is null)
        {
            csv.Write(",,");
            return;
        }

        csv.Write(',');
        csv.Write(sample.Value.MeanAllocatedBytes.ToString("G17", CultureInfo.InvariantCulture));
        csv.Write(',');
        csv.Write(sample.Value.StdDevAllocatedBytes.ToString("G17", CultureInfo.InvariantCulture));
    }

    private readonly record struct Sample(
        double MeanMilliseconds,
        double StdDevMilliseconds,
        double MeanAllocatedBytes,
        double StdDevAllocatedBytes);

    private readonly record struct Row(string Label, string Key, Sample Cfits, Sample? Sharp, Sample? Astropy);
}
