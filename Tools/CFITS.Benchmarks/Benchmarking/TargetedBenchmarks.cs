using System.Buffers.Binary;
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
    private static readonly int[] EndianValues = Enumerable.Range(0, 1_048_576).Select(index => index * 3).ToArray();
    private static readonly byte[] EndianEncoded = new byte[1_048_576 * sizeof(int)];
    private static readonly int[] EndianDecoded = new int[1_048_576];
    private static readonly FitsWorld BenchmarkWorld = CreateWorld();
    private static readonly FitsClock BenchmarkClock = FitsClock.FromHeader(
        FitsHeader.Builder().Text("TIMESYS", "TT").Text("TIMEUNIT", "s").Real("MJDREF", 50000).Build());
    private static readonly FitsDocument BitDocument = CreateBitDocument();
    private static readonly FitsDocument BulkTableDocument = CreateBulkTableDocument();
    private static readonly FitsDocument RealImageDocument = CreateRealImageDocument();
    private static readonly byte[] PlioPayload = CreatePlioPayload();
    private static readonly FitsDocument PlioDocument = CreatePlioDocument();
    private static readonly byte[] Gzip2UnshuffleInput = CreateGzip2UnshuffleInput();
    private static readonly byte[] LongStringHeader = CreateLongStringHeader();
    private static readonly byte[] OrderedContinuationHeader = CreateOrderedContinuationHeader();
    private static readonly byte[] CommentaryHeader = CreateCommentaryHeader();

    public static void Run()
    {
        string root = FitsScenarios.FindRoot();
        string data = Path.Combine(root, "Benchmarks", "Data");
        string results = Path.Combine(root, "Benchmarks", "Results", "optimization-targeted.csv");
        var scenarios = CreateScenarios(data)
            .Where(scenario => scenario.Family is "pixels" or "compression" or "parse-open-header" or "wcs" or "clock" or "checksum" or "micro-arithmetic" or "tables" or "source-generator-variable" or "runtime-mapping-variable")
            .ToArray();
        string? selected = Environment.GetEnvironmentVariable("CFITS_TARGETED_SCENARIO");
        if (!string.IsNullOrWhiteSpace(selected))
        {
            scenarios = scenarios.Where(scenario => scenario.Name.Equals(selected, StringComparison.OrdinalIgnoreCase)).ToArray();
        }

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

        TargetedScenario[] scenarios =
        [
            new("pixel_sum", "pixels", () => SumImage(image256)),
            new("pixel_endian_vector", "pixels", EndianVector),
            new("pixel_endian_scalar", "pixels", EndianScalar),
            new("pixel_bulk_copy", "pixels", () => CopyImage(image256)),
            new("pixel_real_identity_copy", "pixels", CopyRealImage),
            new("pixel_stored_copy", "pixels", () => CopyStoredImage(image256)),
            new("pixel_shuffle_decode", "pixels", () => SumImage(gzipShuffled, 1)),
            new("gzip2_unshuffle_generic", "compression", Gzip2UnshuffleGeneric),
            new("gzip2_unshuffle_width4", "compression", Gzip2UnshuffleWidth4),
            new("pixel_offset_multiply", "micro-arithmetic", PixelOffsetMultiply),
            new("pixel_offset_counter", "micro-arithmetic", PixelOffsetCounter),
            new("gzip_decode", "compression", () => SumImage(gzip, 1)),
            new("plio_decode", "compression", ReadPlioImage),
            new("plio_decode_word_buffer_baseline", "compression", DecodePlioWordBufferBaseline),
            new("tile_index_division", "micro-arithmetic", TileIndexDivision),
            new("tile_index_counter", "micro-arithmetic", TileIndexCounter),
            new("rice_decode", "compression", () => SumImage(rice, 1)),
            new("hcompress_decode", "compression", () => SumImage(hcompress, 1)),
            new("checksum_verify", "checksum", () => VerifyChecksum(checksum)),
            new("checksum_split_division", "micro-arithmetic", ChecksumSplitDivision),
            new("checksum_split_bitwise", "micro-arithmetic", ChecksumSplitBitwise),
            new("table_bit_packing", "tables", PackBits),
            new("table_bit_read", "tables", ReadBits),
            new("table_ascii_writer", "tables", WriteAsciiTable),
            new("table_ascii_integer_parse", "tables", ReadAsciiInteger),
            new("table_ascii_real_parse", "tables", ReadAsciiReal),
            new("table_direct_writer", "tables", WriteBinaryTable),
            new("table_binary_scalar_writer", "tables", WriteBinaryScalarTable),
            new("table_binary_vector_writer", "tables", WriteBinaryVectorTable),
            new("table_mixed_row_writer", "tables", WriteMixedTable),
            new("table_variable_builder", "tables", WriteVariableTable),
            new("table_variable_integer_vector", "tables", ReadVariableIntegerVector),
            new("table_variable_real_vector", "tables", ReadVariableRealVector),
            new("table_heap_memory_view", "tables", ReadHeapMemory),
            new("table_compressed_nocompress_variable", "tables", ReadCompressedNocompressTable),
            new("table_raw_owned_builder", "tables", WriteOwnedRawTable),
            new("table_bulk_scalar_read", "tables", ReadBulkScalarTable),
            new("table_scalar_row_read", "tables", ReadScalarRows),
            new("parse_open", "parse-open-header", () => Open(tiny)),
            new("header_lookup", "parse-open-header", () => HeaderLookup(tiny)),
            new("header_parser_commentary_cards", "parse-open-header", ParseCommentaryHeader),
            new("header_long_string_continue", "parse-open-header", ParseLongStringHeader),
            new("header_continue_ordering", "parse-open-header", ParseOrderedContinuationHeader),
            new("document_multi_hdu_write", "parse-open-header", WriteBoundaryHdus),
            new("wcs_projection", "wcs", () => _sink = EvaluateWcs()),
            new("wcs_inverse_allocating", "wcs", () => { _sink = EvaluateWcsInverse(); }),
            new("wcs_inverse_span", "wcs", () => { _sink = EvaluateWcsInverseSpan(); }),
            new("clock_unit_cached", "clock", () => { _sink = EvaluateClock(); }),
            new("checksum_sum_1mb", "checksum", () => { _sink = FitsChecksum.Sum(ChecksumPayload); }),
            new("write_allocation", "write-read-allocations", () => _sink = WriteImage()),
            new("read_allocation", "write-read-allocations", () => ReadImage(image256)),
        ];
        return scenarios
        .Concat(GeneratedSerializationBenchmarks.CreateScenarios())
        .ToArray();
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

    private static void ReadBits()
    {
        FitsColumn column = BitDocument.Hdus[1].Table!.Column("flags");
        long sum = 0;
        for (int index = 0; index < 8_192; index++)
        {
            sum += column.ReadInteger(0, index);
        }

        _sink = sum;
    }

    private static FitsDocument CreateBitDocument()
    {
        bool[] values = Enumerable.Range(0, 8_192)
            .Select(static index => (index & 3) == 0)
            .ToArray();
        return FitsDocumentBuilder.Create()
            .BinaryTable(new BinaryTableBuilder().AddBits("flags", values.Length, [values]))
            .Build();
    }

    private static FitsDocument CreateBulkTableDocument()
    {
        const int rows = 10_000;
        return FitsDocumentBuilder.Create()
            .BinaryTable(new BinaryTableBuilder()
                .Add("integer", Enumerable.Range(0, rows).ToArray())
                .Add("real", Enumerable.Range(0, rows).Select(static i => (double)i).ToArray()))
            .Build();
    }

    private static FitsDocument CreateRealImageDocument()
    {
        const int pixels = 256 * 256;
        var values = new float[pixels];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = index * 0.25f;
        }

        return FitsDocumentBuilder.Create().Image([256, 256], values).Build();
    }

    private static void ReadBulkScalarTable()
    {
        FitsTable table = BulkTableDocument.Hdus[1].Table!;
        var integers = new long[table.RowCount];
        var reals = new double[table.RowCount];
        table.Column("integer").TryCopyInteger(integers);
        table.Column("real").TryCopyReal(reals);
        _sink = integers[^1] + reals[^1];
    }

    private static void ReadScalarRows()
    {
        FitsTable table = BulkTableDocument.Hdus[1].Table!;
        FitsColumn integer = table.Column("integer");
        FitsColumn real = table.Column("real");
        long integerTotal = 0;
        double realTotal = 0;
        for (int row = 0; row < table.RowCount; row++)
        {
            integerTotal += integer.ReadInteger(row);
            realTotal += real.ReadReal(row);
        }

        _sink = integerTotal + realTotal;
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

    private static void WriteBinaryScalarTable()
    {
        const int rows = 10_000;
        _sink = FitsDocumentBuilder.Create()
            .BinaryTable(new BinaryTableBuilder()
                .Add("integer", Enumerable.Range(0, rows).ToArray())
                .Add("real", Enumerable.Range(0, rows).Select(static i => (double)i).ToArray()))
            .Build()
            .ToArray();
    }

    private static void ReadAsciiInteger()
    {
        using var document = FitsDocumentBuilder.Create()
            .AsciiTable(new AsciiTableBuilder().AddInteger("value", Enumerable.Range(0, 1_000).Select(static i => (long)i).ToArray(), 8))
            .Build();
        FitsColumn column = document.Hdus[1].Table!.Column("value");
        long total = 0;
        for (int row = 0; row < 1_000; row++)
            total += column.ReadInteger(row);
        _sink = total;
    }

    private static void ReadAsciiReal()
    {
        using var document = FitsDocumentBuilder.Create()
            .AsciiTable(new AsciiTableBuilder().AddReal("value", Enumerable.Range(0, 1_000).Select(static i => i * 0.25).ToArray(), 12, 4, 'E'))
            .Build();
        FitsColumn column = document.Hdus[1].Table!.Column("value");
        double total = 0;
        for (int row = 0; row < 1_000; row++)
            total += column.ReadReal(row);
        _sink = total;
    }

    private static void WriteBinaryVectorTable()
    {
        const int rows = 2_000;
        _sink = FitsDocumentBuilder.Create()
            .BinaryTable(new BinaryTableBuilder()
                .AddVector("values", 8, Enumerable.Range(0, rows * 8).ToArray(), [2, 4]))
            .Build()
            .ToArray();
    }

    private static void WriteMixedTable()
    {
        const int rows = 2_000;
        _sink = FitsDocumentBuilder.Create()
            .BinaryTable(new BinaryTableBuilder()
                .Add("fixed", Enumerable.Range(0, rows).ToArray())
                .AddVariable("variable", Enumerable.Range(0, rows)
                    .Select(static i => Enumerable.Range(0, i % 8).ToArray()).ToArray()))
            .Build()
            .ToArray();
    }

    private static void WriteVariableTable()
    {
        const int rows = 2_000;
        var values = Enumerable.Range(0, rows)
            .Select(static i => Enumerable.Range(0, i % 32).Select(static value => (double)value).ToArray())
            .ToArray();
        _sink = FitsDocumentBuilder.Create().BinaryTable(new BinaryTableBuilder().AddVariable("values", values)).Build().ToArray();
    }

    private static void ReadVariableIntegerVector()
    {
        using var document = FitsDocumentBuilder.Create()
            .BinaryTable(new BinaryTableBuilder().AddVariable("values", new[] { new[] { 1, 2, 3, 4 } }))
            .Build();
        _sink = document.Hdus[1].Table!.Column("values").ReadIntegerVector(0);
    }

    private static void ReadVariableRealVector()
    {
        using var document = FitsDocumentBuilder.Create()
            .BinaryTable(new BinaryTableBuilder().AddVariable("values", new[] { new[] { 1d, 2d, 3d, 4d } }))
            .Build();
        _sink = document.Hdus[1].Table!.Column("values").ReadRealVector(0);
    }

    private static void ReadHeapMemory()
    {
        using var document = FitsDocumentBuilder.Create()
            .BinaryTable(new BinaryTableBuilder().AddVariable("values", new[] { new[] { 1, 2, 3, 4 } }))
            .Build();
        _sink = document.Hdus[1].Table!.Column("values").ReadVariableMemory(0);
    }

    private static void ReadCompressedNocompressTable()
    {
        byte[] payload = [0, 0, 0, 1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0, 4];
        const int bundleLength = 32;
        byte[] bundle = new byte[bundleLength + payload.Length];
        BinaryPrimitives.WriteInt32BigEndian(bundle.AsSpan(0, 4), payload.Length);
        BinaryPrimitives.WriteInt32BigEndian(bundle.AsSpan(4, 4), bundleLength);
        BinaryPrimitives.WriteInt32BigEndian(bundle.AsSpan(8, 4), 4);
        BinaryPrimitives.WriteInt32BigEndian(bundle.AsSpan(12, 4), 0);
        payload.CopyTo(bundle.AsSpan(bundleLength));
        string bytes = new(bundle.Select(static value => (char)value).ToArray());

        using var document = FitsDocumentBuilder.Create()
            .BinaryTable(
                new BinaryTableBuilder().AddVariable("COMPRESSED", new[] { bytes }),
                header => header
                    .Logical("ZTABLE", true)
                    .Integer("ZNAXIS1", 8)
                    .Integer("ZNAXIS2", 1)
                    .Integer("ZPCOUNT", payload.Length)
                    .Integer("ZTILELEN", 1)
                    .Text("ZFORM1", "1PJ")
                    .Text("ZCTYP1", "NOCOMPRESS"))
            .Build();
        _sink = document.Hdus[1].Table!.Column("COMPRESSED").ReadVariableBytes(0);
    }

    private static void ReadPlioImage()
    {
        int[] values = new int[4_096];
        PlioDocument.Hdus[1].Image!.CopyInteger(values);
        _sink = values[^1];
    }

    private static void DecodePlioWordBufferBaseline()
    {
        ReadOnlySpan<byte> source = PlioPayload;
        var words = new int[(source.Length / 2) + 1];
        for (int index = 0; index < source.Length / 2; index++)
        {
            words[index + 1] = (short)((source[index * 2] << 8) | source[(index * 2) + 1]);
        }

        int length = words[3];
        var output = new int[4_096];
        int x = 1;
        int outputIndex = 0;
        int high = 1;
        bool skipNext = false;
        for (int instruction = 4; instruction <= length; instruction++)
        {
            if (skipNext)
            {
                skipNext = false;
                continue;
            }

            int opcode = words[instruction] / 4096;
            int data = words[instruction] & 4095;
            switch (opcode)
            {
                case 0:
                case 4:
                case 5:
                    int end = x + data - 1;
                    int count = Math.Min(end, output.Length) - x + 1;
                    if (count > 0)
                    {
                        int fill = opcode == 4 ? high : 0;
                        output.AsSpan(outputIndex, count).Fill(fill);
                        if (opcode == 5)
                        {
                            output[outputIndex + count - 1] = high;
                        }

                        outputIndex += count;
                    }

                    x = end + 1;
                    break;
                case 1:
                    high = (words[instruction + 1] << 12) + data;
                    skipNext = true;
                    break;
                case 2:
                    high += data;
                    break;
                case 3:
                    high -= data;
                    break;
                case 6:
                    output[outputIndex++] = high += data;
                    x++;
                    break;
                case 7:
                    output[outputIndex++] = high -= data;
                    x++;
                    break;
            }
        }

        _sink = output[^1];
    }

    private static FitsDocument CreatePlioDocument()
    {
        string payload = new(PlioPayload.Select(static value => (char)value).ToArray());
        return FitsDocumentBuilder.Create()
            .BinaryTable(
                new BinaryTableBuilder().AddVariable("COMPRESSED_DATA", [payload]),
                header => header
                    .Logical("ZIMAGE", true)
                    .Text("ZCMPTYPE", "PLIO_1")
                    .Integer("ZBITPIX", 32)
                    .Integer("ZNAXIS", 1)
                    .Integer("ZNAXIS1", 4_096)
                    .Integer("ZTILE1", 4_096))
            .Build();
    }

    private static byte[] CreatePlioPayload()
    {
        short[] words = [0, 0, 7, Instruction(0, 2_048), Instruction(1, 123), 0, Instruction(4, 2_048)];
        var bytes = new byte[words.Length * sizeof(short)];
        for (int index = 0; index < words.Length; index++)
        {
            bytes[index * 2] = (byte)(words[index] >> 8);
            bytes[(index * 2) + 1] = (byte)words[index];
        }

        return bytes;
    }

    private static short Instruction(int opcode, int data) =>
        unchecked((short)((opcode << 12) | data));

    private static void WriteOwnedRawTable()
    {
        _sink = new FitsRawTableBuilder(false)
            .AddColumn("value", "1J")
            .AddRowOwned([0, 0, 0, 1])
            .SetHeapOwned([])
            .Build()
            .ToArray();
    }

    private static int Open(string path)
    {
        using var document = FitsDocument.Open(path);
        return document.Hdus.Count;
    }

    private static void WriteBoundaryHdus()
    {
        _sink = FitsDocumentBuilder.Create()
            .Image([1], new byte[] { 1 })
            .Image([2880], new byte[2880])
            .Image([2881], new byte[2881])
            .Build()
            .ToArray();
    }

    private static long HeaderLookup(string path)
    {
        using var document = FitsDocument.Open(path);
        return document.Hdus[0].Header.GetInt64("NAXIS1");
    }

    private static void ParseLongStringHeader()
    {
        using var document = FitsDocument.Parse(LongStringHeader);
        _sink = document.Hdus[0].Header.GetString("LONGSTR");
    }

    private static void ParseOrderedContinuationHeader()
    {
        using var document = FitsDocument.Parse(OrderedContinuationHeader);
        _sink = document.Hdus[0].Header.Cards[^1].Keyword;
    }

    private static void ParseCommentaryHeader()
    {
        using var document = FitsDocument.Parse(CommentaryHeader);
        _sink = document.Hdus[0].Header.Cards[^1].Text;
    }

    private static byte[] CreateLongStringHeader() =>
        FitsDocumentBuilder.Create()
            .Image(Array.Empty<int>(), Array.Empty<byte>(), header => header.Text("LONGSTR", new string('x', 4_096)))
            .Build()
            .ToArray();

    private static byte[] CreateOrderedContinuationHeader() =>
        FitsDocumentBuilder.Create()
            .Image(Array.Empty<int>(), Array.Empty<byte>(), header => header
                .Integer("BEFORE", 1)
                .Text("LONGSTR", new string('x', 1_024), "continued")
                .Integer("AFTER", 2)
                .Comment("kept"))
            .Build()
            .ToArray();

    private static byte[] CreateCommentaryHeader() =>
        FitsDocumentBuilder.Create()
            .Image(Array.Empty<int>(), Array.Empty<byte>(), header => header
                .Comment("commentary")
                .History("history")
                .Blank("blank"))
            .Build()
            .ToArray();

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

    private static void CopyRealImage()
    {
        FitsImage image = RealImageDocument.Hdus[0].Image!;
        var destination = new double[image.Length];
        image.CopyPhysical(destination);
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

    private static void Gzip2UnshuffleGeneric()
    {
        const int width = 4;
        int count = Gzip2UnshuffleInput.Length / width;
        byte[] output = new byte[Gzip2UnshuffleInput.Length];
        for (int item = 0; item < count; item++)
        {
            int sourceOffset = item;
            int destinationOffset = item * width;
            for (int byteIndex = 0; byteIndex < width; byteIndex++)
            {
                output[destinationOffset + byteIndex] = Gzip2UnshuffleInput[sourceOffset];
                sourceOffset += count;
            }
        }

        _sink = output[^1];
    }

    private static void Gzip2UnshuffleWidth4()
    {
        const int width = 4;
        int count = Gzip2UnshuffleInput.Length / width;
        byte[] output = new byte[Gzip2UnshuffleInput.Length];
        int destinationOffset = 0;
        for (int item = 0; item < count; item++)
        {
            int sourceOffset = item;
            output[destinationOffset++] = Gzip2UnshuffleInput[sourceOffset];
            output[destinationOffset++] = Gzip2UnshuffleInput[sourceOffset + count];
            output[destinationOffset++] = Gzip2UnshuffleInput[sourceOffset + (count * 2)];
            output[destinationOffset++] = Gzip2UnshuffleInput[sourceOffset + (count * 3)];
        }

        _sink = output[^1];
    }

    private static byte[] CreateGzip2UnshuffleInput()
    {
        const int width = 4;
        const int count = 65_536;
        var shuffled = new byte[width * count];
        for (int plane = 0; plane < width; plane++)
        {
            for (int item = 0; item < count; item++)
            {
                shuffled[(plane * count) + item] = (byte)(item + (plane * 17));
            }
        }

        return shuffled;
    }

    private static bool VerifyChecksum(string path)
    {
        using var document = FitsDocument.Open(path, new FitsOpenOptions { VerifyChecksum = true });
        return document.Hdus[0].Integrity.IsValid;
    }

    private static void EndianVector()
    {
        FitsEndian.WriteInt32(EndianValues, EndianEncoded);
        FitsEndian.ReadInt32(EndianEncoded, EndianDecoded);
        _sink = EndianDecoded[^1];
    }

    private static void EndianScalar()
    {
        for (int index = 0; index < EndianValues.Length; index++)
        {
            BinaryPrimitives.WriteInt32BigEndian(EndianEncoded.AsSpan(index * sizeof(int), sizeof(int)), EndianValues[index]);
        }

        for (int index = 0; index < EndianDecoded.Length; index++)
        {
            EndianDecoded[index] = BinaryPrimitives.ReadInt32BigEndian(EndianEncoded.AsSpan(index * sizeof(int), sizeof(int)));
        }

        _sink = EndianDecoded[^1];
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
