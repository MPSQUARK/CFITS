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
    private const int PlotWidth = 1500;
    private const int PanelHeight = 420;
    private const int Columns = 3;
    private const int RowsPerPage = 5;
    private const double LogarithmBase = 10;
    private static readonly TimeSpan MinimumIteration = TimeSpan.FromMilliseconds(100);
    private static readonly Color AxisColor = Color.FromHex("#7b8da6");
    private static readonly Color GridColor = Color.FromHex("#d8e0ec").WithAlpha(0.8);
    private static readonly Color CfitsColor = Color.FromHex("#0d8f8a");
    private static readonly Color AstropyColor = Color.FromHex("#1e4d8c");
    private static readonly Color CSharpFitsColor = Color.FromHex("#e0634a");

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
        string directory = Path.Combine(root, "Benchmarks", "Data");
        string[] sdssFiles = SdssFiles(root);
        string sdss = sdssFiles[0];
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
        string astropyCsv = Path.Combine(root, "Benchmarks", "Results", "astropy-scenarios.csv");
        RunAstropy(root, directory, sdssFiles, astropyCsv);
        var astropy = ReadAstropy(astropyCsv);
        for (int index = 0; index < measured.Count; index++)
        {
            var row = measured[index];
            measured[index] = row with { Astropy = astropy.GetValueOrDefault(row.Key) };
        }

        string png = Path.Combine(root, "Benchmarks", "Plots", "fits-comparison.png");
        string csv = Path.Combine(root, "Benchmarks", "Results", "fits-comparison.csv");
        Save(measured, png, csv);
        Console.WriteLine($"Plot: {png}");
        Console.WriteLine($"Table: {csv}");

        SaveReadWriteComparison(
            sdssFiles,
            astropy,
            Path.Combine(root, "Benchmarks", "Plots", "fits-comparison-read-write.png"),
            Path.Combine(root, "Benchmarks", "Results", "fits-comparison-read-write.csv"));
    }

    private static void SaveReadWriteComparison(
        IReadOnlyList<string> files,
        IReadOnlyDictionary<string, Sample> astropy,
        string pngPath,
        string csvPath)
    {
        var measurements = new[]
        {
            ToReadWriteMeasurement("CFITS read", CfitsColor, Measure(() => CfitsReadWhole(files))),
            ToReadWriteMeasurement("CFITS write", CfitsColor, Measure(() => CfitsWriteWhole(files))),
            AstropyMeasurement("Astropy read", AstropyColor, astropy, "sdss_read_all"),
            AstropyMeasurement("Astropy write", AstropyColor, astropy, "sdss_write_all"),
            MeasureSafe(() => SharpReadWhole(files)) is Sample sharpRead
                ? ToReadWriteMeasurement("CSharpFITS read", CSharpFitsColor, sharpRead)
                : new Measurement("CSharpFITS read", CSharpFitsColor, 0, 0, false),
            MeasureSafe(() => SharpWriteWhole(files)) is Sample sharpWrite
                ? ToReadWriteMeasurement("CSharpFITS write", CSharpFitsColor, sharpWrite)
                : new Measurement("CSharpFITS write", CSharpFitsColor, 0, 0, false),
        };

        var plot = new Plot();
        AddComparisonPanel(
            plot,
            "Read/write five SDSS FITS files",
            "Mean time (μs), log10; <1 μs clamped to 0",
            measurements);
        SavePlot(pngPath, path => plot.SavePng(path, PlotWidth, PanelHeight));

        using var csv = new StreamWriter(csvPath);
        csv.WriteLine("category,mean_ms,stddev_ms,status");
        foreach (Measurement measurement in measurements)
        {
            csv.Write(measurement.Name);
            csv.Write(',');
            if (!measurement.Available)
            {
                csv.WriteLine(",,unsupported");
                continue;
            }

            csv.Write((measurement.Mean / 1_000).ToString("G17", CultureInfo.InvariantCulture));
            csv.Write(',');
            csv.Write((measurement.StandardDeviation / 1_000).ToString("G17", CultureInfo.InvariantCulture));
            csv.WriteLine(",available");
        }
        Console.WriteLine($"Read/write plot: {pngPath}");
        Console.WriteLine($"Read/write table: {csvPath}");
    }

    private static Measurement AstropyMeasurement(
        string name,
        Color color,
        IReadOnlyDictionary<string, Sample> samples,
        string key)
    {
        return samples.TryGetValue(key, out Sample sample)
            ? ToReadWriteMeasurement(name, color, sample)
            : new Measurement(name, color, 0, 0, false);
    }

    private static Measurement ToReadWriteMeasurement(string name, Color color, Sample sample) =>
        new(name, color, sample.MeanMilliseconds * 1_000, sample.StdDevMilliseconds * 1_000, true);

    private static void CfitsReadWhole(IReadOnlyList<string> files)
    {
        foreach (string file in files)
        {
            using var document = FitsDocument.Open(file);
            GC.KeepAlive(document);
        }
    }

    private static void CfitsWriteWhole(IReadOnlyList<string> files)
    {
        foreach (string file in files)
        {
            using var document = FitsDocument.Open(file);
            _ = document.ToArray();
        }
    }

    private static void SharpReadWhole(IReadOnlyList<string> files)
    {
        foreach (string file in files)
        {
            var fits = new Fits(file);
            _ = fits.Read();
            fits.Close();
        }
    }

    private static void SharpWriteWhole(IReadOnlyList<string> files)
    {
        foreach (string file in files)
        {
            var fits = new Fits(file);
            _ = fits.Read();
            using var stream = new MemoryStream();
            fits.Write(stream);
            fits.Close();
        }
    }

    public static void Replot(string root)
    {
        string directory = Path.Combine(root, "Benchmarks");
        string results = Path.Combine(directory, "Results");
        string plots = Path.Combine(directory, "Plots");
        string csvPath = Path.Combine(results, "fits-comparison.csv");
        string replotSuffix = "-replotted";
        var rows = ReadComparisonCsv(csvPath);
        Save(
            rows,
            Path.Combine(plots, $"fits-comparison{replotSuffix}.png"),
            Path.Combine(results, $"fits-comparison{replotSuffix}.csv"));
        ReplotReadWrite(
            Path.Combine(results, "fits-comparison-read-write.csv"),
            Path.Combine(plots, $"fits-comparison-read-write{replotSuffix}.png"));
        Console.WriteLine($"Replotted existing benchmark data from: {csvPath}");
    }

    private static void ReplotReadWrite(string csvPath, string pngPath)
    {
        var colors = new Dictionary<string, Color>(StringComparer.Ordinal)
        {
            ["CFITS read"] = CfitsColor,
            ["CFITS write"] = CfitsColor,
            ["Astropy read"] = AstropyColor,
            ["Astropy write"] = AstropyColor,
            ["CSharpFITS read"] = CSharpFitsColor,
            ["CSharpFITS write"] = CSharpFitsColor,
        };
        var measurements = File.ReadLines(csvPath)
            .Skip(1)
            .Select(line => line.Split(','))
            .Select(fields => new Measurement(
                fields[0],
                colors[fields[0]],
                fields.Length > 3 && fields[3].Equals("unsupported", StringComparison.OrdinalIgnoreCase)
                    ? 0
                    : double.Parse(fields[1], CultureInfo.InvariantCulture) * 1_000,
                fields.Length > 3 && fields[3].Equals("unsupported", StringComparison.OrdinalIgnoreCase)
                    ? 0
                    : fields.Length > 2 ? double.Parse(fields[2], CultureInfo.InvariantCulture) * 1_000 : 0,
                fields.Length <= 3 || !fields[3].Equals("unsupported", StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        var plot = new Plot();
        AddComparisonPanel(plot, "Read/write five SDSS FITS files", "Mean time (μs), log10; <1 μs clamped to 0", measurements);
        SavePlot(pngPath, path => plot.SavePng(path, PlotWidth, PanelHeight));
    }

    private static IReadOnlyList<Row> ReadComparisonCsv(string path)
    {
        var rows = new List<Row>();
        foreach (string line in File.ReadLines(path).Skip(1))
        {
            string[] fields = line.Split(',');
            bool hasSharpSample = !line.EndsWith(",,", StringComparison.Ordinal);
            int dataFieldCount = hasSharpSample ? 12 : 10;
            int dataStart = fields.Length - dataFieldCount;
            if (dataStart < 1)
            {
                throw new InvalidDataException($"Invalid benchmark row in {path}: {line}");
            }

            string label = string.Join(',', fields[..dataStart]);
            int index = dataStart;
            Sample cfits = ReadSample(fields, ref index);
            Sample? astropy = ReadSample(fields, ref index);
            Sample? sharp = hasSharpSample ? ReadSample(fields, ref index) : null;
            rows.Add(new Row(label, string.Empty, cfits, sharp, astropy));
        }

        return rows;
    }

    private static Sample ReadSample(string[] fields, ref int index)
    {
        if (index + 3 >= fields.Length ||
            !double.TryParse(fields[index], NumberStyles.Float, CultureInfo.InvariantCulture, out double meanMilliseconds) ||
            !double.TryParse(fields[index + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double standardDeviationMilliseconds) ||
            !double.TryParse(fields[index + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out double meanAllocatedBytes) ||
            !double.TryParse(fields[index + 3], NumberStyles.Float, CultureInfo.InvariantCulture, out double standardDeviationAllocatedBytes))
        {
            throw new InvalidDataException("Invalid numeric benchmark sample.");
        }

        index += 4;
        return new Sample(meanMilliseconds, standardDeviationMilliseconds, meanAllocatedBytes, standardDeviationAllocatedBytes);
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
        _ = document.Hdus[hdu].Image!.SumPhysical();
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
            for (int element = 0; element < column.Count(row); element++)
            {
                sum += column.ReadInteger(row, element);
            }
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
        Span<double> pixel = stackalloc double[2];
        Span<double> coordinates = stackalloc double[2];
        double sum = 0;
        for (int i = 0; i < 100_000; i++)
        {
            pixel[0] = (i % 400) + 1;
            pixel[1] = (i / 400) + 1;
            world.PixelToWorld(pixel, coordinates);
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
            sum += (long)SumArray((Array)column.GetValue(row)!);
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

    private static string[] SdssFiles(string root) =>
    [
        Path.Combine(root, "SampleData", "Real", "sdss-spec-0266.fits"),
        Path.Combine(root, "SampleData", "Real", "sdss-spec-0285.fits"),
        Path.Combine(root, "SampleData", "Real", "sdss-spec-0500-001.fits"),
        Path.Combine(root, "SampleData", "Real", "sdss-spec-0500-002.fits"),
        Path.Combine(root, "SampleData", "Real", "sdss-spec-0500-003.fits"),
    ];

    private static void RunAstropy(string root, string directory, IReadOnlyList<string> sdssFiles, string csvPath)
    {
        string python = PythonRuntime.Find(root);
        string script = Path.Combine(root, "Benchmarks", "Scripts", "compare_astropy.py");
        var start = new ProcessStartInfo(python)
        {
            WorkingDirectory = root,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(script);
        start.ArgumentList.Add(directory);
        foreach (string sdssFile in sdssFiles)
        {
            start.ArgumentList.Add(sdssFile);
        }
        start.ArgumentList.Add(csvPath);
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
        var series = new (string Name, Color Color, Func<Row, Sample?> Value)[]
        {
            ("CFITS", CfitsColor, row => row.Cfits),
            ("Astropy", AstropyColor, row => row.Astropy),
            ("CSharpFITS", CSharpFitsColor, row => row.Sharp),
        };

        var ordered = rows.OrderBy(row => row.Cfits.MeanMilliseconds).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(pngPath)!);
        SaveComparisonPages(ordered, series, pngPath, row => row.Cfits.MeanMilliseconds, "Mean time (μs), log10; <1 μs clamped to 0");

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

        string memorySuffix = Path.GetFileNameWithoutExtension(pngPath).EndsWith("-replotted", StringComparison.Ordinal)
            ? "-replotted"
            : string.Empty;
        string memoryPngPath = Path.Combine(Path.GetDirectoryName(pngPath)!, $"fits-comparison-memory{memorySuffix}.png");
        string memoryCsvPath = Path.Combine(Path.GetDirectoryName(csvPath)!, $"fits-comparison-memory{memorySuffix}.csv");
        SaveMemory(rows, memoryPngPath, memoryCsvPath);
        Console.WriteLine($"Memory plot: {memoryPngPath}");
        Console.WriteLine($"Memory table: {memoryCsvPath}");
    }

    private static Multiplot CreateMultiplot(int panelCount)
    {
        var multiplot = new Multiplot();
        multiplot.AddPlots(panelCount);
        var layout = new ScottPlot.MultiplotLayouts.CustomGrid();
        for (int index = 0; index < panelCount; index++)
        {
            layout.Set(
                multiplot.GetPlot(index),
                new GridCell(index / Columns, index % Columns, RowsPerPage, Columns, 1, 1));
        }
        multiplot.Layout = layout;
        return multiplot;
    }

    private static void SaveComparisonPages(
        IReadOnlyList<Row> rows,
        IReadOnlyList<(string Name, Color Color, Func<Row, Sample?> Value)> series,
        string pngPath,
        Func<Row, double> order,
        string axisLabel,
        bool memory = false)
    {
        var ordered = rows.OrderBy(order).ToArray();
        for (int page = 0; page * RowsPerPage * Columns < ordered.Length; page++)
        {
            int start = page * RowsPerPage * Columns;
            int count = Math.Min(RowsPerPage * Columns, ordered.Length - start);
            var multiplot = CreateMultiplot(count);
            for (int index = 0; index < count; index++)
            {
                Row row = ordered[start + index];
                var measurements = series
                    .Select(item =>
                    {
                        Sample? sample = item.Value(row);
                        return sample is null
                            ? new Measurement(item.Name, item.Color, 0, 0, false)
                            : memory
                                ? new Measurement(item.Name, item.Color, sample.Value.MeanAllocatedBytes / 1024, sample.Value.StdDevAllocatedBytes / 1024, true)
                                : new Measurement(item.Name, item.Color, sample.Value.MeanMilliseconds * 1_000, sample.Value.StdDevMilliseconds * 1_000, true);
                    })
                    .ToArray();
                AddComparisonPanel(multiplot.GetPlot(index), row.Label, axisLabel, measurements);
            }

            string pagePath = page == 0
                ? pngPath
                : Path.Combine(
                    Path.GetDirectoryName(pngPath)!,
                    $"{Path.GetFileNameWithoutExtension(pngPath)}-page-{page + 1}.png");
            SavePlot(pagePath, path => multiplot.SavePng(path, PlotWidth, PanelHeight * RowsPerPage));
        }
    }

    private static void SavePlot(string path, Action<string> save)
    {
        try
        {
            save(path);
        }
        catch (IOException)
        {
            string fallback = Path.Combine(
                Path.GetDirectoryName(path)!,
                Path.GetFileNameWithoutExtension(path) + "-new.png");
            save(fallback);
            Console.WriteLine($"Existing plot was locked. Wrote {fallback}");
        }
    }

    private static void AddComparisonPanel(
        Plot plot,
        string title,
        string axisLabel,
        IReadOnlyList<Measurement> measurements)
    {
        plot.FigureBackground.Color = Colors.White;
        plot.DataBackground.Color = Colors.White;
        plot.Axes.Color(AxisColor);
        plot.Grid.MajorLineColor = GridColor;

        AddBars(plot, measurements);

        plot.Axes.Bottom.SetTicks(
            Enumerable.Range(0, measurements.Count).Select(index => (double)index).ToArray(),
            measurements.Select(measurement => measurement.Name).ToArray());
        plot.Axes.Bottom.TickLabelStyle.FontSize = 10;
        ConfigureLogAxis(plot, measurements.SelectMany(measurement => new[] { measurement.Mean, measurement.Mean + measurement.StandardDeviation }));
        plot.Axes.Left.Label.Text = axisLabel;
        plot.Axes.Title.Label.Text = title;
        plot.Axes.Title.Label.FontSize = 12;
        plot.Axes.Margins(left: 0, right: 0.08, bottom: 0.04, top: 0.04);
    }

    private static void AddBars(Plot plot, IReadOnlyList<Measurement> measurements)
    {
        foreach ((Measurement measurement, int index) in measurements.Select((measurement, index) => (measurement, index)))
        {
            if (!measurement.Available)
            {
                continue;
            }

            double value = Transform(measurement.Mean);
            double lower = Transform(Math.Max(measurement.Mean - measurement.StandardDeviation, 0));
            double upper = Transform(measurement.Mean + measurement.StandardDeviation);
            var plotted = plot.Add.Bars(new[]
            {
                new Bar
                {
                    Position = index,
                    Value = value,
                    Error = Math.Max(value - lower, upper - value),
                    Size = 0.65,
                    ErrorSize = 0.12,
                },
            });
            plotted.Color = measurement.Color;
        }
    }

    private static void ConfigureLogAxis(Plot plot, IEnumerable<double> upperValues)
    {
        double maximum = Math.Max(upperValues.Max(), 1);
        int lastExponent = Math.Max(0, (int)Math.Ceiling(Math.Log10(maximum)));
        int tickStride = Math.Max(1, (int)Math.Ceiling((lastExponent + 1) / 8d));
        var tickValues = new List<double> { 0 };
        for (int exponent = 0; exponent <= lastExponent; exponent += tickStride)
        {
            tickValues.Add(Math.Pow(LogarithmBase, exponent));
        }

        double[] positions = tickValues.Select(Transform).ToArray();
        string[] labels = tickValues
            .Select(value => value == 0 ? "0–1" : value.ToString("G4", CultureInfo.InvariantCulture))
            .ToArray();
        plot.Axes.Left.SetTicks(positions, labels);
        plot.Axes.SetLimitsY(0, Math.Max(1, Transform(maximum)) * 1.08);
    }

    private static double Transform(double value) => value <= 1 ? 0 : Math.Log10(value);

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
        var series = new (string Name, Color Color, Func<Row, Sample?> Value)[]
        {
            ("CFITS", CfitsColor, row => row.Cfits),
            ("Astropy (Python peak)", AstropyColor, row => row.Astropy),
            ("CSharpFITS", CSharpFitsColor, row => row.Sharp),
        };
        var ordered = rows.OrderBy(row => row.Cfits.MeanAllocatedBytes).ToArray();
        SaveComparisonPages(
            ordered,
            series,
            pngPath,
            row => row.Cfits.MeanAllocatedBytes,
            "Mean allocated memory (KiB), log10; <1 KiB clamped to 0",
            memory: true);

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

    private readonly record struct Measurement(string Name, Color Color, double Mean, double StandardDeviation, bool Available);

    private readonly record struct Sample(
        double MeanMilliseconds,
        double StdDevMilliseconds,
        double MeanAllocatedBytes,
        double StdDevAllocatedBytes);

    private readonly record struct Row(string Label, string Key, Sample Cfits, Sample? Sharp, Sample? Astropy);
}
