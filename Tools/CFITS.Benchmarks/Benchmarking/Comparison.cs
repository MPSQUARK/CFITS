using System.Diagnostics;
using System.Globalization;
using System.Text;
using nom.tam.fits;
using ScottPlot;

namespace CFITS.Benchmarks;

/// <summary>
/// Times the same files with CFITS, source-generated CFITS, Astropy, and CSharpFITS.
/// Each iteration repeats the operation until at least 100 ms have elapsed, matching a short BenchmarkDotNet iteration.
/// </summary>
internal static class Comparison
{
    private const int Warmup = 2;
    private const int Iterations = 6;
    private const int PlotWidth = 1500;
    private const int PanelHeight = 420;
    private const int Columns = 3;
    private static readonly TimeSpan MinimumIteration = TimeSpan.FromMilliseconds(100);
    private static readonly Color AxisColor = Color.FromHex("#7b8da6");
    private static readonly Color GridColor = Color.FromHex("#d8e0ec").WithAlpha(0.8);
    private static readonly Color CfitsColor = Color.FromHex("#0d8f8a");
    private static readonly Color GeneratedColor = Color.FromHex("#7b4ab5");
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
        string[] fireflyFiles = FireflyFiles(root).Where(File.Exists).ToArray();
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
        var scenarios = new (string Label, Action Cfits, Action? Generated, Action? Sharp, string Astropy)[]
        {
            ("Open a 4×4 image", () => CfitsOpen(files["tiny"]), () => GeneratedReadShortImage(files["tiny"]), () => SharpOpen(files["tiny"]), "open_tiny"),
            ("Read a 256² int16 image", () => CfitsSum(files["image256"]), () => GeneratedSumShortImage(files["image256"]), () => SharpSum(files["image256"]), "read_image256"),
            ("Read a 1024² float32 image", () => CfitsSum(files["image1024"]), () => GeneratedSumFloatImage(files["image1024"]), () => SharpSum(files["image1024"]), "read_image1024"),
            ("Scan 20,000 binary-table rows", () => CfitsColumn(files["binary"], "flux"), () => GeneratedSumFlux(files["binary"]), () => SharpColumn(files["binary"], "flux"), "scan_binary"),
            ("Read a 4,000-row ASCII table", () => CfitsColumn(files["ascii"], "value"), () => GeneratedSumAscii(files["ascii"]), () => SharpColumn(files["ascii"], "value"), "read_ascii"),
            ("Read 1,000 variable-length rows", () => CfitsVariable(files["heap"]), () => GeneratedSumVariable(files["heap"]), () => SharpVariable(files["heap"]), "read_heap"),
            ("Write a 512² int16 image", CfitsWrite, GeneratedWrite, SharpWrite, "write_image"),
            ("Decompress a GZIP_1 image", () => CfitsSum(files["gzip"], 1), () => GeneratedSumCompressed(files["gzip"]), null, "decompress_gzip"),
            ("Decompress a GZIP_2 image", () => CfitsSum(files["gzip2"], 1), () => GeneratedSumCompressed(files["gzip2"]), null, "decompress_gzip2"),
            ("Decompress a Rice image", () => CfitsSum(files["rice"], 1), () => GeneratedSumCompressed(files["rice"]), null, "decompress_rice"),
            ("Decompress an H-compress image", () => CfitsSum(files["hcomp"], 1), () => GeneratedSumCompressed(files["hcomp"]), null, "decompress_hcomp"),
            ("Open a checksummed image", () => CfitsChecksum(files["checksum"]), () => GeneratedReadChecksum(files["checksum"]), null, "open_checksum"),
            ("TAN coordinates, 100,000 pixels", () => CfitsWcs(world), null, null, "tan_wcs"),
            ("Read an SDSS spectrum column", () => CfitsColumn(files["sdss"], "flux"), () => GeneratedSumFlux(files["sdss"]), () => SharpColumn(files["sdss"], "flux"), "read_sdss"),
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
            Sample? generated = scenario.Generated is null ? null : MeasureSafe(scenario.Generated, "CFITS (SG)");
            Sample? sharp = scenario.Sharp is null ? null : MeasureSafe(scenario.Sharp);
            measured.Add(new Row(scenario.Label, scenario.Astropy, cfits, generated, sharp, null));
        }

        Console.WriteLine("Measuring Astropy...");
        string astropyCsv = Path.Combine(Path.GetTempPath(), $"cfits-astropy-{Guid.NewGuid():N}.csv");
        Dictionary<string, Sample> astropy;
        try
        {
            RunAstropy(root, directory, sdssFiles, fireflyFiles, astropyCsv);
            astropy = ReadAstropy(astropyCsv);
        }
        catch (Exception exception)
        {
            Console.WriteLine($"  Astropy skipped: {exception.Message}");
            astropy = new Dictionary<string, Sample>(StringComparer.Ordinal);
        }
        finally
        {
            File.Delete(astropyCsv);
        }
        for (int index = 0; index < measured.Count; index++)
        {
            var row = measured[index];
            measured[index] = row with { Astropy = astropy.GetValueOrDefault(row.Key) };
        }

        string csv = Path.Combine(root, "Benchmarks", "Results", "fits-comparison.csv");
        var readWrite = MeasureReadWrite(sdssFiles, fireflyFiles, astropy);
        SaveResults(measured, readWrite, csv);
        Console.WriteLine($"Table: {csv}");
    }

    private static IReadOnlyList<Row> MeasureReadWrite(
        IReadOnlyList<string> sdssFiles,
        IReadOnlyList<string> fireflyFiles,
        IReadOnlyDictionary<string, Sample> astropy)
    {
        var rows = new List<Row>();
        AddReadWriteRows(rows, "SDSS", sdssFiles, astropy, "sdss");
        if (fireflyFiles.Count == 2)
        {
            AddReadWriteRows(rows, "Firefly", fireflyFiles, astropy, "firefly");
        }
        return rows;
    }

    private static void AddReadWriteRows(
        ICollection<Row> rows,
        string label,
        IReadOnlyList<string> files,
        IReadOnlyDictionary<string, Sample> astropy,
        string keyPrefix)
    {
        var measurements = new (string Suffix, Action Cfits, Action? Generated, Action Sharp, string AstropyKey)[]
        {
            ("read", () => CfitsReadWhole(files), () => GeneratedReadWhole(files), () => SharpReadWhole(files), $"{keyPrefix}_read_all"),
            ("write", () => CfitsWriteWhole(files), () => GeneratedWriteWhole(files), () => SharpWriteWhole(files), $"{keyPrefix}_write_all"),
        };
        foreach (var measurement in measurements)
        {
            Sample cfits = Measure(measurement.Cfits);
            Sample? generated = measurement.Generated is null ? null : MeasureSafe(measurement.Generated, "CFITS (SG)");
            Sample? sharp = MeasureSafe(measurement.Sharp);
            astropy.TryGetValue(measurement.AstropyKey, out Sample astropySample);
            rows.Add(new Row($"{label} whole-file {measurement.Suffix}", measurement.AstropyKey, cfits, generated, sharp,
                astropy.ContainsKey(measurement.AstropyKey) ? astropySample : null, true));
        }
    }

    private static void CfitsReadWhole(IReadOnlyList<string> files)
    {
        foreach (string file in files)
        {
            using var document = FitsDocument.Open(file);
            GC.KeepAlive(document);
        }
    }

    private static void GeneratedReadWhole(IReadOnlyList<string> files)
    {
        for (int index = 0; index < files.Count; index++)
        {
            using var stream = File.OpenRead(files[index]);
            GeneratedWholeFile value = FitsSerializer.Read(
                stream,
                GeneratedBenchmarkContext.Default.GeneratedWholeFile);
            GC.KeepAlive(value.Hdus.Count);
        }
    }

    private static void GeneratedWriteWhole(IReadOnlyList<string> files)
    {
        for (int index = 0; index < files.Count; index++)
        {
            using var input = File.OpenRead(files[index]);
            GeneratedWholeFile value = FitsSerializer.Read(
                input,
                GeneratedBenchmarkContext.Default.GeneratedWholeFile);
            using var output = new MemoryStream();
            FitsSerializer.Write(
                output,
                value,
                GeneratedBenchmarkContext.Default.GeneratedWholeFile);
            GC.KeepAlive(output.Length);
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
        string results = Path.Combine(root, "Benchmarks", "Results");
        string csvPath = Path.Combine(results, "fits-comparison.csv");
        var (regular, readWrite) = ReadComparisonCsv(csvPath);
        SavePlots(regular, readWrite, root);
        Console.WriteLine($"Plots: {Path.Combine(root, "Benchmarks", "Plots")}");
    }

    private static (IReadOnlyList<Row> Regular, IReadOnlyList<Row> ReadWrite) ReadComparisonCsv(string path)
    {
        var regular = new List<Row>();
        var readWrite = new List<Row>();
        foreach (string line in File.ReadLines(path).Skip(1))
        {
            string[] fields = ParseCsvLine(line);
            if (fields.Length is not (14 or 18))
                throw new InvalidDataException($"Invalid benchmark row in {path}: {line}");
            int index = 2;
            Sample cfits = ReadSample(fields, ref index);
            Sample? generated = fields.Length == 18 ? ReadOptionalSample(fields, ref index) : null;
            Sample? astropy = ReadOptionalSample(fields, ref index);
            Sample? sharp = ReadOptionalSample(fields, ref index);
            var row = new Row(fields[1], fields[1], cfits, generated, sharp, astropy, fields[0] == "read-write");
            (row.IsReadWrite ? readWrite : regular).Add(row);
        }
        return (regular, readWrite);
    }

    private static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;
        for (int index = 0; index < line.Length; index++)
        {
            char character = line[index];
            if (character == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (character == ',' && !quoted)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else
            {
                field.Append(character);
            }
        }

        fields.Add(field.ToString());
        return fields.ToArray();
    }

    private static Sample? ReadOptionalSample(string[] fields, ref int index) =>
        string.IsNullOrEmpty(fields[index]) ? SkipSample(fields, ref index) : ReadSample(fields, ref index);

    private static Sample? SkipSample(string[] fields, ref int index)
    {
        index += 4;
        return null;
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

    private static Sample? MeasureSafe(Action action, string implementation = "CSharpFITS")
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
            Console.WriteLine($"  {implementation} skipped: {exception.GetType().Name}: {exception.Message}");
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

    private static void GeneratedReadShortImage(string path)
    {
        using var stream = File.OpenRead(path);
        GeneratedShortImage image = FitsSerializer.Read(
            stream,
            GeneratedBenchmarkContext.Default.GeneratedShortImage);
        _ = image.Pixels!.Length;
    }

    private static void GeneratedSumShortImage(string path)
    {
        using var stream = File.OpenRead(path);
        GeneratedShortImage image = FitsSerializer.Read(
            stream,
            GeneratedBenchmarkContext.Default.GeneratedShortImage);
        short[,] pixels = image.Pixels!;
        long sum = 0;
        for (int row = 0; row < pixels.GetLength(0); row++)
        {
            for (int column = 0; column < pixels.GetLength(1); column++)
            {
                sum += pixels[row, column];
            }
        }

        GC.KeepAlive(sum);
    }

    private static void GeneratedSumFloatImage(string path)
    {
        using var stream = File.OpenRead(path);
        GeneratedFloatImage image = FitsSerializer.Read(
            stream,
            GeneratedBenchmarkContext.Default.GeneratedFloatImage);
        float[,] pixels = image.Pixels!;
        double sum = 0;
        for (int row = 0; row < pixels.GetLength(0); row++)
        {
            for (int column = 0; column < pixels.GetLength(1); column++)
            {
                sum += pixels[row, column];
            }
        }

        GC.KeepAlive(sum);
    }

    private static void GeneratedSumFlux(string path)
    {
        using var stream = File.OpenRead(path);
        IReadOnlyList<GeneratedFluxRow> rows = FitsSerializer.Read(
            stream,
            GeneratedBenchmarkContext.Default.GeneratedFluxRow);
        double sum = 0;
        for (int index = 0; index < rows.Count; index++)
        {
            sum += rows[index].Flux;
        }

        GC.KeepAlive(sum);
    }

    private static void GeneratedSumAscii(string path)
    {
        using var stream = File.OpenRead(path);
        IReadOnlyList<GeneratedAsciiRow> rows = FitsSerializer.Read(
            stream,
            GeneratedBenchmarkContext.Default.GeneratedAsciiRow);
        double sum = 0;
        for (int index = 0; index < rows.Count; index++)
        {
            sum += rows[index].Value;
        }

        GC.KeepAlive(sum);
    }

    private static void GeneratedSumVariable(string path)
    {
        using var stream = File.OpenRead(path);
        IReadOnlyList<GeneratedVariableRow> rows = FitsSerializer.Read(
            stream,
            GeneratedBenchmarkContext.Default.GeneratedVariableRow);
        long sum = 0;
        for (int row = 0; row < rows.Count; row++)
        {
            int[] samples = rows[row].Samples;
            for (int index = 0; index < samples.Length; index++)
            {
                sum += samples[index];
            }
        }

        GC.KeepAlive(sum);
    }

    private static void GeneratedSumCompressed(string path)
    {
        using var stream = File.OpenRead(path);
        GeneratedCompressedImage image = FitsSerializer.Read(
            stream,
            GeneratedBenchmarkContext.Default.GeneratedCompressedImage);
        float[,] pixels = image.Pixels!;
        double sum = 0;
        for (int row = 0; row < pixels.GetLength(0); row++)
        {
            for (int column = 0; column < pixels.GetLength(1); column++)
            {
                sum += pixels[row, column];
            }
        }

        GC.KeepAlive(sum);
    }

    private static void GeneratedReadChecksum(string path)
    {
        GeneratedReadShortImage(path);
    }

    private static void GeneratedReadFloatImage(string path)
    {
        using var stream = File.OpenRead(path);
        GeneratedFloatImage image = FitsSerializer.Read(
            stream,
            GeneratedBenchmarkContext.Default.GeneratedFloatImage);
        _ = image.Pixels?.Length;
    }

    private static void GeneratedWrite()
    {
        var image = new GeneratedShortImage { Pixels = new short[512, 512] };
        short[,] pixels = image.Pixels;
        for (int row = 0; row < pixels.GetLength(0); row++)
        {
            for (int column = 0; column < pixels.GetLength(1); column++)
            {
                pixels[row, column] = (short)(((row * 512) + column) % 1000);
            }
        }

        using var stream = new MemoryStream();
        FitsSerializer.Write(
            stream,
            image,
            GeneratedBenchmarkContext.Default.GeneratedShortImage);
        _ = stream.Length;
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

    private static string[] FireflyFiles(string root) =>
    [
        Path.Combine(root, "SampleData", "Real", "SFD_dust_4096_ngp.fits"),
        Path.Combine(root, "SampleData", "Real", "SFD_dust_4096_sgp.fits"),
    ];

    private static void RunAstropy(
        string root,
        string directory,
        IReadOnlyList<string> sdssFiles,
        IReadOnlyList<string> fireflyFiles,
        string csvPath)
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
        if (fireflyFiles.Count == 2)
        {
            start.ArgumentList.Add("--firefly");
            foreach (string fireflyFile in fireflyFiles)
            {
                start.ArgumentList.Add(fireflyFile);
            }
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

    private static void SaveResults(
        IReadOnlyList<Row> rows,
        IReadOnlyList<Row> readWrite,
        string csvPath)
    {
        var allRows = rows.Concat(readWrite).ToArray();
        using var csv = new StreamWriter(csvPath);
        csv.WriteLine("kind,scenario,cfits_mean_ms,cfits_stddev_ms,cfits_managed_allocated_bytes,cfits_managed_allocated_stddev_bytes,cfits_sg_mean_ms,cfits_sg_stddev_ms,cfits_sg_managed_allocated_bytes,cfits_sg_managed_allocated_stddev_bytes,astropy_mean_ms,astropy_stddev_ms,astropy_python_peak_bytes,astropy_python_peak_stddev_bytes,csharpfits_mean_ms,csharpfits_stddev_ms,csharpfits_managed_allocated_bytes,csharpfits_managed_allocated_stddev_bytes");
        foreach (var row in allRows)
        {
            csv.Write(row.IsReadWrite ? "read-write," : "regular,");
            csv.Write(EscapeCsv(row.Label));
            WriteSample(csv, row.Cfits);
            WriteSample(csv, row.Generated);
            WriteSample(csv, row.Astropy);
            WriteSample(csv, row.Sharp);
            csv.WriteLine();
        }
    }

    private static void SavePlots(
        IReadOnlyList<Row> rows,
        IReadOnlyList<Row> readWrite,
        string root)
    {
        var allRows = rows.Concat(readWrite).ToArray();
        var series = new (string Name, Color Color, Func<Row, Sample?> Value)[]
        {
            ("CFITS", CfitsColor, row => row.Cfits),
            ("CFITS (SG)", GeneratedColor, row => row.Generated),
            ("Astropy", AstropyColor, row => row.Astropy),
            ("CSharpFITS", CSharpFitsColor, row => row.Sharp),
        };
        string plots = Path.Combine(root, "Benchmarks", "Plots");
        Directory.CreateDirectory(plots);
        SaveComparison(
            allRows,
            series,
            Path.Combine(plots, "fits-comparison.png"),
            row => row.Cfits.MeanMilliseconds,
            "Time (μs)");
        SaveComparison(
            allRows,
            series,
            Path.Combine(plots, "fits-comparison-memory.png"),
            row => row.Cfits.MeanAllocatedBytes,
            "Allocated memory (KiB)",
            memory: true);
    }

    private static string EscapeCsv(string value) =>
        value.Contains(',', StringComparison.Ordinal) || value.Contains('"', StringComparison.Ordinal)
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;

    private static void SaveComparison(
        IReadOnlyList<Row> rows,
        IReadOnlyList<(string Name, Color Color, Func<Row, Sample?> Value)> series,
        string pngPath,
        Func<Row, double> order,
        string axisLabel,
        bool memory = false)
    {
        var ordered = rows.OrderBy(row => row.IsReadWrite).ThenBy(order).ToArray();
        var multiplot = CreateMultiplot(ordered.Length);
        for (int index = 0; index < ordered.Length; index++)
        {
            Row row = ordered[index];
            var measurements = series.Select(item =>
            {
                Sample? sample = item.Value(row);
                return sample is null
                    ? new Measurement(item.Name, item.Color, 0, 0, false)
                    : memory
                        ? new Measurement(item.Name, item.Color, sample.Value.MeanAllocatedBytes / 1024, sample.Value.StdDevAllocatedBytes / 1024, true)
                        : new Measurement(item.Name, item.Color, sample.Value.MeanMilliseconds * 1_000, sample.Value.StdDevMilliseconds * 1_000, true);
            }).ToArray();
            AddComparisonPanel(multiplot.GetPlot(index), row.Label, axisLabel, measurements);
        }

        int rowsPerImage = (int)Math.Ceiling(ordered.Length / (double)Columns);
        SavePlot(pngPath, path => multiplot.SavePng(path, PlotWidth, PanelHeight * rowsPerImage));
    }

    private static Multiplot CreateMultiplot(int panelCount)
    {
        var multiplot = new Multiplot();
        multiplot.AddPlots(panelCount);
        var layout = new ScottPlot.MultiplotLayouts.CustomGrid();
        int rows = (int)Math.Ceiling(panelCount / (double)Columns);
        for (int index = 0; index < panelCount; index++)
        {
            layout.Set(
                multiplot.GetPlot(index),
                new GridCell(index / Columns, index % Columns, rows, Columns, 1, 1));
        }
        multiplot.Layout = layout;
        return multiplot;
    }

    private static void SavePlot(string path, Action<string> save)
    {
        string temporaryPath = path + ".tmp";
        try
        {
            File.Delete(temporaryPath);
            save(temporaryPath);
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"Could not replace benchmark plot '{path}'.", exception);
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
        ConfigureLinearAxis(plot, measurements.SelectMany(measurement => new[] { measurement.Mean, measurement.Mean + measurement.StandardDeviation }));
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

            double value = measurement.Mean;
            double lower = Math.Max(measurement.Mean - measurement.StandardDeviation, 0);
            double upper = measurement.Mean + measurement.StandardDeviation;
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

    private static void ConfigureLinearAxis(Plot plot, IEnumerable<double> upperValues)
    {
        double maximum = Math.Max(upperValues.DefaultIfEmpty().Max(), 0);
        plot.Axes.SetLimitsY(0, Math.Max(1, maximum) * 1.08);
    }

    private static void WriteSample(StreamWriter csv, Sample? sample)
    {
        if (sample is null)
        {
            csv.Write(",,,,");
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

    private readonly record struct Measurement(string Name, Color Color, double Mean, double StandardDeviation, bool Available);

    private readonly record struct Sample(
        double MeanMilliseconds,
        double StdDevMilliseconds,
        double MeanAllocatedBytes,
        double StdDevAllocatedBytes);

    private readonly record struct Row(
        string Label,
        string Key,
        Sample Cfits,
        Sample? Generated,
        Sample? Sharp,
        Sample? Astropy,
        bool IsReadWrite = false);
}
