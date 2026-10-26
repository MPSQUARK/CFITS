namespace CFITS.Benchmarks;

internal static class GeneratedSerializationBenchmarks
{
    private static object? _sink;

    public static IReadOnlyList<TargetedScenario> CreateScenarios()
    {
        GeneratedExposure imageValue = CreateImageValue();
        byte[] image = CreateImage();
        GeneratedCompositeFile compositeValue = CreateCompositeValue(imageValue);
        byte[] composite = CreateComposite(compositeValue);
        byte[] variableDouble = CreateVariableDoubleTable();
        byte[] variableInt = CreateVariableIntTable();
        var scenarios = new List<TargetedScenario>
        {
            new("generated_image_write", "source-generator", () => WriteGeneratedImage(imageValue)),
            new("generated_image_builder_write", "source-generator-builder", () => WriteGeneratedImageBuilder(imageValue)),
            new("generated_image_write_staged", "source-generator-staged", () => WriteGeneratedImageStaged(imageValue)),
            new("generated_image_read", "source-generator", () => ReadGeneratedImage(image)),
            new("runtime_image_read", "runtime-mapping", () => ReadRuntimeImage(image)),
            new("generated_composite_write", "source-generator-composite", () => WriteGeneratedComposite(compositeValue)),
            new("generated_composite_builder_write", "source-generator-composite-builder", () => WriteGeneratedCompositeBuilder(compositeValue)),
            new("generated_composite_write_staged", "source-generator-composite-staged", () => WriteGeneratedCompositeStaged(compositeValue)),
            new("generated_composite_read", "source-generator-composite", () => ReadGeneratedComposite(composite)),
            new("generated_variable_double_read", "source-generator-variable", () => ReadGeneratedVariableDouble(variableDouble)),
            new("runtime_variable_double_read", "runtime-mapping-variable", () => ReadRuntimeVariableDouble(variableDouble)),
            new("runtime_variable_double_scalar_read", "runtime-mapping-variable", () => ReadRuntimeVariableDoubleScalar(variableDouble)),
            new("generated_variable_int_read", "source-generator-variable", () => ReadGeneratedVariableInt(variableInt)),
            new("runtime_variable_int_vector_convert_read", "runtime-mapping-variable", () => ReadRuntimeVariableIntVectorConvert(variableInt)),
            new("runtime_variable_int_scalar_read", "runtime-mapping-variable", () => ReadRuntimeVariableIntScalar(variableInt)),
        };
        foreach (int rowCount in new[] { 1_000, 20_000, 100_000 })
        {
            GeneratedEventRow[] rows = CreateTableRows(rowCount);
            byte[] table = CreateTable(rowCount);
            string label = rowCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            scenarios.Add(new($"generated_table_write_{label}_rows", "source-generator", () => WriteGeneratedTable(rows)));
            scenarios.Add(new($"generated_table_builder_write_{label}_rows", "source-generator-builder", () => WriteGeneratedTableBuilder(rows)));
            scenarios.Add(new($"generated_table_write_staged_{label}_rows", "source-generator-staged", () => WriteGeneratedTableStaged(rows)));
            scenarios.Add(new($"generated_table_read_{label}_rows", "source-generator", () => ReadGeneratedTable(table)));
            scenarios.Add(new($"runtime_table_scalar_{label}_rows", "runtime-mapping-scalar", () => ReadRuntimeTableScalar(table)));
            scenarios.Add(new($"runtime_table_bulk_{label}_rows", "runtime-mapping-bulk", () => ReadRuntimeTableBulk(table)));
        }

        return scenarios;
    }

    private static GeneratedCompositeFile CreateCompositeValue(GeneratedExposure image)
    {
        return new GeneratedCompositeFile
        {
            Image = image,
            Events = CreateTableRows(1_000),
        };
    }

    private static byte[] CreateComposite(GeneratedCompositeFile value)
    {
        using var stream = new MemoryStream();
        FitsSerializer.Write(stream, value, GeneratedBenchmarkContext.Default.GeneratedCompositeFile);
        return stream.ToArray();
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

    private static byte[] CreateVariableDoubleTable()
    {
        const int rowCount = 20_000;
        var rows = new GeneratedVariableDoubleRow[rowCount];
        for (int row = 0; row < rows.Length; row++)
        {
            rows[row] = new GeneratedVariableDoubleRow
            {
                Values = Enumerable.Range(0, 8).Select(value => row + (value * 0.25)).ToArray(),
            };
        }

        using var stream = new MemoryStream();
        FitsSerializer.Write(stream, rows, GeneratedBenchmarkContext.Default.GeneratedVariableDoubleRow);
        return stream.ToArray();
    }

    private static byte[] CreateVariableIntTable()
    {
        const int rowCount = 20_000;
        var rows = new GeneratedVariableIntRow[rowCount];
        for (int row = 0; row < rows.Length; row++)
        {
            rows[row] = new GeneratedVariableIntRow
            {
                Values = Enumerable.Range(0, 8).Select(value => row + value).ToArray(),
            };
        }

        using var stream = new MemoryStream();
        FitsSerializer.Write(stream, rows, GeneratedBenchmarkContext.Default.GeneratedVariableIntRow);
        return stream.ToArray();
    }

    private static void WriteGeneratedImage(GeneratedExposure value)
    {
        using var stream = new MemoryStream();
        FitsSerializer.Write(stream, value, GeneratedBenchmarkContext.Default.GeneratedExposure);
        _sink = stream.Length;
    }

    private static void WriteGeneratedImageBuilder(GeneratedExposure value)
    {
        var builder = FitsDocumentBuilder.Create();
        FitsSerializer.Write(builder, value, GeneratedBenchmarkContext.Default.GeneratedExposure);
        _sink = builder.Build().ToArray().Length;
    }

    private static void WriteGeneratedTable(IReadOnlyList<GeneratedEventRow> rows)
    {
        using var stream = new MemoryStream();
        FitsSerializer.Write(stream, rows, GeneratedBenchmarkContext.Default.GeneratedEventRow);
        _sink = stream.Length;
    }

    private static void WriteGeneratedTableBuilder(IReadOnlyList<GeneratedEventRow> rows)
    {
        var builder = FitsDocumentBuilder.Create();
        FitsSerializer.Write(builder, rows, GeneratedBenchmarkContext.Default.GeneratedEventRow);
        _sink = builder.Build().ToArray().Length;
    }

    private static void WriteGeneratedComposite(GeneratedCompositeFile value)
    {
        using var stream = new MemoryStream();
        FitsSerializer.Write(stream, value, GeneratedBenchmarkContext.Default.GeneratedCompositeFile);
        _sink = stream.Length;
    }

    private static void WriteGeneratedCompositeBuilder(GeneratedCompositeFile value)
    {
        var builder = FitsDocumentBuilder.Create();
        FitsSerializer.Write(builder, value.Image, GeneratedBenchmarkContext.Default.GeneratedExposure);
        FitsSerializer.Write(builder, value.Events, GeneratedBenchmarkContext.Default.GeneratedEventRow);
        _sink = builder.Build().ToArray().Length;
    }

    private static void WriteGeneratedCompositeStaged(GeneratedCompositeFile value)
    {
        using var stream = new MemoryStream();
        CreateImageBuilder(value.Image)
            .BinaryTable(
                new BinaryTableBuilder()
                    .Add("TIME", value.Events.Select(row => row.Time).ToArray())
                    .Add("PHA", value.Events.Select(row => row.Channel).ToArray()),
                header => header.Text("EXTNAME", "EVENTS"))
            .Build()
            .Write(stream);
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

    private static void ReadGeneratedComposite(byte[] data)
    {
        using var stream = new MemoryStream(data, writable: false);
        GeneratedCompositeFile value = FitsSerializer.Read(
            stream, GeneratedBenchmarkContext.Default.GeneratedCompositeFile);
        _sink = value.Image.ExposureSeconds + value.Events[0].Channel + value.Events[^1].Time;
    }

    private static void ReadGeneratedVariableDouble(byte[] data)
    {
        using var stream = new MemoryStream(data, writable: false);
        IReadOnlyList<GeneratedVariableDoubleRow> rows = FitsSerializer.Read(
            stream, GeneratedBenchmarkContext.Default.GeneratedVariableDoubleRow);
        _sink = rows[0].Values[0] + rows[^1].Values[^1];
    }

    private static void ReadRuntimeVariableDouble(byte[] data)
    {
        using var stream = new MemoryStream(data, writable: false);
        using var document = FitsDocument.Open(stream);
        FitsTable table = document.Hdus[1].Table!;
        FitsColumn column = table.Column("VALUES");
        double first = 0;
        double last = 0;
        for (int row = 0; row < table.RowCount; row++)
        {
            double[] values = column.ReadRealVector(row);
            if (row == 0)
            {
                first = values[0];
            }

            last = values[^1];
        }

        _sink = first + last;
    }

    private static void ReadRuntimeVariableDoubleScalar(byte[] data)
    {
        using var stream = new MemoryStream(data, writable: false);
        using var document = FitsDocument.Open(stream);
        FitsTable table = document.Hdus[1].Table!;
        FitsColumn column = table.Column("VALUES");
        double first = 0;
        double last = 0;
        for (int row = 0; row < table.RowCount; row++)
        {
            int count = column.Count(row);
            double rowFirst = 0;
            double rowLast = 0;
            for (int element = 0; element < count; element++)
            {
                double value = column.ReadReal(row, element);
                if (element == 0)
                {
                    rowFirst = value;
                }

                rowLast = value;
            }

            if (row == 0)
            {
                first = rowFirst;
            }

            last = rowLast;
        }

        _sink = first + last;
    }

    private static void ReadGeneratedVariableInt(byte[] data)
    {
        using var stream = new MemoryStream(data, writable: false);
        IReadOnlyList<GeneratedVariableIntRow> rows = FitsSerializer.Read(
            stream, GeneratedBenchmarkContext.Default.GeneratedVariableIntRow);
        _sink = rows[0].Values[0] + rows[^1].Values[^1];
    }

    private static void ReadRuntimeVariableIntVectorConvert(byte[] data)
    {
        using var stream = new MemoryStream(data, writable: false);
        using var document = FitsDocument.Open(stream);
        FitsTable table = document.Hdus[1].Table!;
        FitsColumn column = table.Column("VALUES");
        int first = 0;
        int last = 0;
        for (int row = 0; row < table.RowCount; row++)
        {
            long[] source = column.ReadIntegerVector(row);
            var values = new int[source.Length];
            for (int element = 0; element < source.Length; element++)
            {
                values[element] = (int)source[element];
            }

            if (row == 0)
            {
                first = values[0];
            }

            last = values[^1];
        }

        _sink = first + last;
    }

    private static void ReadRuntimeVariableIntScalar(byte[] data)
    {
        using var stream = new MemoryStream(data, writable: false);
        using var document = FitsDocument.Open(stream);
        FitsTable table = document.Hdus[1].Table!;
        FitsColumn column = table.Column("VALUES");
        int first = 0;
        int last = 0;
        for (int row = 0; row < table.RowCount; row++)
        {
            int count = column.Count(row);
            int rowFirst = 0;
            int rowLast = 0;
            for (int element = 0; element < count; element++)
            {
                int value = (int)column.ReadInteger(row, element);
                if (element == 0)
                {
                    rowFirst = value;
                }

                rowLast = value;
            }

            if (row == 0)
            {
                first = rowFirst;
            }

            last = rowLast;
        }

        _sink = first + last;
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
