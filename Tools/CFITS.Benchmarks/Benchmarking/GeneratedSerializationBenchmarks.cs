namespace CFITS.Benchmarks;

internal static class GeneratedSerializationBenchmarks
{
    private static object? _sink;

    public static IReadOnlyList<TargetedScenario> CreateScenarios()
    {
        GeneratedExposure imageValue = CreateImageValue();
        byte[] image = CreateImage();
        var scenarios = new List<TargetedScenario>
        {
            new("generated_image_write", "source-generator", () => WriteGeneratedImage(imageValue)),
            new("generated_image_write_staged", "source-generator-staged", () => WriteGeneratedImageStaged(imageValue)),
            new("generated_image_read", "source-generator", () => ReadGeneratedImage(image)),
            new("runtime_image_read", "runtime-mapping", () => ReadRuntimeImage(image)),
        };
        foreach (int rowCount in new[] { 1_000, 20_000, 100_000 })
        {
            GeneratedEventRow[] rows = CreateTableRows(rowCount);
            byte[] table = CreateTable(rowCount);
            string label = rowCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            scenarios.Add(new($"generated_table_write_{label}_rows", "source-generator", () => WriteGeneratedTable(rows)));
            scenarios.Add(new($"generated_table_write_staged_{label}_rows", "source-generator-staged", () => WriteGeneratedTableStaged(rows)));
            scenarios.Add(new($"generated_table_read_{label}_rows", "source-generator", () => ReadGeneratedTable(table)));
            scenarios.Add(new($"runtime_table_scalar_{label}_rows", "runtime-mapping-scalar", () => ReadRuntimeTableScalar(table)));
            scenarios.Add(new($"runtime_table_bulk_{label}_rows", "runtime-mapping-bulk", () => ReadRuntimeTableBulk(table)));
        }

        return scenarios;
    }

    private static GeneratedExposure CreateImageValue()
    {
        var value = new GeneratedExposure
        {
            ExposureSeconds = 12.5,
            Pixels = new float[64, 64],
        };
        for (int row = 0; row < value.Pixels.GetLength(0); row++)
        {
            for (int column = 0; column < value.Pixels.GetLength(1); column++)
            {
                value.Pixels[row, column] = row + (column * 0.25f);
            }
        }

        return value;
    }

    private static byte[] CreateImage()
    {
        using var stream = new MemoryStream();
        FitsSerializer.Write(stream, CreateImageValue(), GeneratedBenchmarkContext.Default.GeneratedExposure);
        return stream.ToArray();
    }

    private static GeneratedEventRow[] CreateTableRows(int rowCount)
    {
        var rows = new GeneratedEventRow[rowCount];
        for (int index = 0; index < rows.Length; index++)
        {
            rows[index] = new GeneratedEventRow
            {
                Time = index * 0.5,
                Channel = index % 32,
            };
        }

        return rows;
    }

    private static byte[] CreateTable(int rowCount)
    {
        using var stream = new MemoryStream();
        FitsSerializer.Write(stream, CreateTableRows(rowCount), GeneratedBenchmarkContext.Default.GeneratedEventRow);
        return stream.ToArray();
    }

    private static void WriteGeneratedImage(GeneratedExposure value)
    {
        using var stream = new MemoryStream();
        FitsSerializer.Write(stream, value, GeneratedBenchmarkContext.Default.GeneratedExposure);
        _sink = stream.Length;
    }

    private static void WriteGeneratedTable(IReadOnlyList<GeneratedEventRow> rows)
    {
        using var stream = new MemoryStream();
        FitsSerializer.Write(stream, rows, GeneratedBenchmarkContext.Default.GeneratedEventRow);
        _sink = stream.Length;
    }

    private static void WriteGeneratedImageStaged(GeneratedExposure value)
    {
        using var stream = new MemoryStream();
        CreateImageBuilder(value).Build().Write(stream);
        _sink = stream.Length;
    }

    private static void WriteGeneratedTableStaged(IReadOnlyList<GeneratedEventRow> rows)
    {
        using var stream = new MemoryStream();
        CreateTableBuilder(rows).Build().Write(stream);
        _sink = stream.Length;
    }

    private static FitsDocumentBuilder CreateImageBuilder(GeneratedExposure value)
    {
        float[,] pixels = value.Pixels!;
        int n2 = pixels.GetLength(0);
        int n1 = pixels.GetLength(1);
        var stored = new float[n1 * n2];
        for (int y = 0; y < n2; y++)
        {
            for (int x = 0; x < n1; x++)
            {
                stored[x + (y * n1)] = pixels[y, x];
            }
        }

        return FitsDocumentBuilder.Create().Image([n1, n2], stored, header =>
        {
            header.Real("EXPTIME", value.ExposureSeconds);
            header.Text("EXTNAME", "EXPOSURE");
        });
    }

    private static FitsDocumentBuilder CreateTableBuilder(IReadOnlyList<GeneratedEventRow> rows)
    {
        var times = new double[rows.Count];
        var channels = new int[rows.Count];
        for (int index = 0; index < rows.Count; index++)
        {
            times[index] = rows[index].Time;
            channels[index] = rows[index].Channel;
        }

        return FitsDocumentBuilder.Create()
            .BinaryTable(new BinaryTableBuilder()
                .Add("TIME", times)
                .Add("PHA", channels), header => header.Text("EXTNAME", "EVENTS"));
    }

    private static void ReadGeneratedImage(byte[] data)
    {
        using var stream = new MemoryStream(data, writable: false);
        GeneratedExposure value = FitsSerializer.Read(stream, GeneratedBenchmarkContext.Default.GeneratedExposure);
        _sink = value.ExposureSeconds + value.Pixels![0, 0] + value.Pixels[63, 63];
    }

    private static void ReadRuntimeImage(byte[] data)
    {
        using var stream = new MemoryStream(data, writable: false);
        using var document = FitsDocument.Open(stream);
        FitsHdu hdu = document.Hdus[0];
        FitsImage image = hdu.Image!;
        var pixels = new float[image.Axes[1], image.Axes[0]];
        for (int row = 0; row < pixels.GetLength(0); row++)
        {
            for (int column = 0; column < pixels.GetLength(1); column++)
            {
                pixels[row, column] = (float)image.ReadPhysical(column + (row * pixels.GetLength(1)));
            }
        }

        var value = new GeneratedExposure
        {
            ExposureSeconds = hdu.Header.GetDouble("EXPTIME"),
            Pixels = pixels,
        };
        _sink = value.ExposureSeconds
            + value.Pixels[0, 0]
            + value.Pixels[value.Pixels.GetLength(0) - 1, value.Pixels.GetLength(1) - 1];
    }

    private static void ReadGeneratedTable(byte[] data)
    {
        using var stream = new MemoryStream(data, writable: false);
        IReadOnlyList<GeneratedEventRow> rows = FitsSerializer.Read(stream, GeneratedBenchmarkContext.Default.GeneratedEventRow);
        _sink = rows[0].Time + rows[^1].Channel;
    }

    private static void ReadRuntimeTableScalar(byte[] data)
    {
        using var stream = new MemoryStream(data, writable: false);
        using var document = FitsDocument.Open(stream);
        FitsHdu hdu = document.Hdus.First(candidate =>
            (candidate.Kind is FitsHduKind.BinaryTable or FitsHduKind.AsciiTable)
            && candidate.Header.TryGetCard("EXTNAME", out var extname)
            && extname.GetString() == "EVENTS");
        FitsTable table = hdu.Table!;
        FitsColumn time = table.Column("TIME");
        FitsColumn channel = table.Column("PHA");
        var rows = new GeneratedEventRow[table.RowCount];
        for (int row = 0; row < table.RowCount; row++)
        {
            rows[row] = new GeneratedEventRow
            {
                Time = time.ReadReal(row),
                Channel = (int)channel.ReadInteger(row),
            };
        }

        _sink = rows[0].Time + rows[^1].Channel;
    }

    private static void ReadRuntimeTableBulk(byte[] data)
    {
        using var stream = new MemoryStream(data, writable: false);
        using var document = FitsDocument.Open(stream);
        FitsHdu hdu = document.Hdus.First(candidate =>
            (candidate.Kind is FitsHduKind.BinaryTable or FitsHduKind.AsciiTable)
            && candidate.Header.TryGetCard("EXTNAME", out var extname)
            && extname.GetString() == "EVENTS");
        FitsTable table = hdu.Table!;
        var times = new double[table.RowCount];
        var channels = new long[table.RowCount];
        FitsColumn time = table.Column("TIME");
        FitsColumn channel = table.Column("PHA");
        time.TryCopyReal(times);
        channel.TryCopyInteger(channels);
        var rows = new GeneratedEventRow[table.RowCount];
        for (int row = 0; row < table.RowCount; row++)
        {
            rows[row] = new GeneratedEventRow
            {
                Time = times[row],
                Channel = (int)channels[row],
            };
        }

        _sink = rows[0].Time + rows[^1].Channel;
    }
}
