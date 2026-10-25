namespace CFITS.Tests;

public sealed class GeneratedSerializerTests
{
    [Fact]
    public void GeneratedImageDirectWriteMatchesBuildWrite()
    {
        var value = new Exposure
        {
            ExposureSeconds = 12.5,
            Pixels = new float[,]
            {
                { 1.25f, 2.5f },
                { 3.75f, 5f },
            },
        };

        using var generatedStream = new MemoryStream();
        FitsSerializer.Write(generatedStream, value, AppFitsContext.Default.Exposure);

        using var builtStream = new MemoryStream();
        FitsDocumentBuilder.Create()
            .Image([2, 2], new[] { 1.25f, 2.5f, 3.75f, 5f }, header =>
            {
                header.Real("EXPTIME", value.ExposureSeconds);
                header.Text("EXTNAME", "EXPOSURE");
            })
            .Build()
            .Write(builtStream);

        Assert.Equal(builtStream.ToArray(), generatedStream.ToArray());
        Assert.True(generatedStream.CanWrite);
    }

    [Fact]
    public void GeneratedTableDirectWriteMatchesBuildWrite()
    {
        var rows = new[]
        {
            new EventRow { Time = 1.5, Channel = 4 },
            new EventRow { Time = 2.75, Channel = 9 },
        };

        using var generatedStream = new MemoryStream();
        FitsSerializer.Write(generatedStream, rows, AppFitsContext.Default.EventRow);

        var table = new BinaryTableBuilder()
            .Add("TIME", rows.Select(row => row.Time).ToArray())
            .Add("PHA", rows.Select(row => row.Channel).ToArray());
        using var builtStream = new MemoryStream();
        FitsDocumentBuilder.Create()
            .BinaryTable(table, header => header.Text("EXTNAME", "EVENTS"))
            .Build()
            .Write(builtStream);

        Assert.Equal(builtStream.ToArray(), generatedStream.ToArray());
        Assert.True(generatedStream.CanWrite);
    }

    [Fact]
    public void GeneratedImageSerializerRoundTripsKeywordsAndPixels()
    {
        var value = new Exposure
        {
            ExposureSeconds = 12.5,
            Pixels = new float[,]
            {
                { 1.25f, 2.5f },
                { 3.75f, 5f },
            },
        };

        using var stream = new MemoryStream();
        FitsSerializer.Write(stream, value, AppFitsContext.Default.Exposure);
        stream.Position = 0;

        Exposure result = FitsSerializer.Read(stream, AppFitsContext.Default.Exposure);

        Assert.Equal(value.ExposureSeconds, result.ExposureSeconds);
        Assert.NotNull(result.Pixels);
        Assert.Equal(value.Pixels!.GetLength(0), result.Pixels.GetLength(0));
        Assert.Equal(value.Pixels.GetLength(1), result.Pixels.GetLength(1));
        for (int row = 0; row < value.Pixels.GetLength(0); row++)
        {
            for (int column = 0; column < value.Pixels.GetLength(1); column++)
            {
                Assert.Equal(value.Pixels[row, column], result.Pixels[row, column]);
            }
        }
    }

    [Fact]
    public void GeneratedImageSerializerPassesReadOptionsToDocument()
    {
        using var document = FitsDocumentBuilder.Create()
            .Image([1], new[] { 1f }, header =>
            {
                header.Real("EXPTIME", 1);
                header.Text("EXTNAME", "EXPOSURE");
            })
            .Checksum()
            .Build();
        byte[] corrupted = document.ToArray();
        corrupted[^1] ^= 0x01;

        using var stream = new MemoryStream(corrupted);
        Assert.Throws<FitsException>(() =>
            FitsSerializer.Read(
                stream,
                AppFitsContext.Default.Exposure,
                new FitsOpenOptions { VerifyChecksum = true }));
    }

    [Fact]
    public void GeneratedImageSerializerRejectsAnImageWithTheWrongRank()
    {
        using var document = FitsDocumentBuilder.Create()
            .Image([2], new[] { 1f, 2f }, header => header.Text("EXTNAME", "EXPOSURE"))
            .Build();
        using var stream = new MemoryStream();
        document.Write(stream);
        stream.Position = 0;

        var exception = Assert.Throws<FitsException>(() =>
            FitsSerializer.Read(stream, AppFitsContext.Default.Exposure));

        Assert.Contains("rank", exception.Message);
    }

    [Fact]
    public void GeneratedIntegerImageSerializerPreservesLongValuesBeyondDoublePrecision()
    {
        long[] pixels = [9_007_199_254_740_993, -9_007_199_254_740_993];
        var value = new LongExposure { Pixels = pixels };

        using var stream = new MemoryStream();
        FitsSerializer.Write(stream, value, AppFitsContext.Default.LongExposure);
        stream.Position = 0;

        LongExposure result = FitsSerializer.Read(stream, AppFitsContext.Default.LongExposure);

        Assert.Equal(pixels, result.Pixels);
    }

    [Fact]
    public void GeneratedUnsupportedImageSerializerFailsClearly()
    {
        var value = new UnsupportedImage { Pixels = [true] };

        using var stream = new MemoryStream();
        var writeException = Assert.Throws<NotSupportedException>(() =>
            FitsSerializer.Write(stream, value, AppFitsContext.Default.UnsupportedImage));

        Assert.Contains("only byte, short, int, long, float, and double", writeException.Message);
    }

    [Fact]
    public void GeneratedScalarImageSerializerFailsClearlyOnReadAndWrite()
    {
        var value = new ScalarImage { Pixel = 1.25f };

        using var writeStream = new MemoryStream();
        var writeException = Assert.Throws<NotSupportedException>(() =>
            FitsSerializer.Write(writeStream, value, AppFitsContext.Default.ScalarImage));

        Assert.Contains("requires a supported array", writeException.Message);

        using var document = FitsDocumentBuilder.Create()
            .Image([1], new[] { 1.25f }, header => header.Text("EXTNAME", "SCALAR_IMAGE"))
            .Build();
        using var readStream = new MemoryStream();
        document.Write(readStream);
        readStream.Position = 0;

        var readException = Assert.Throws<NotSupportedException>(() =>
            FitsSerializer.Read(readStream, AppFitsContext.Default.ScalarImage));

        Assert.Contains("requires a supported array", readException.Message);
    }

    [Fact]
    public void GeneratedUnsupportedTableSerializerFailsClearly()
    {
        var rows = new[] { new UnsupportedTableRow { Value = DateTime.UnixEpoch } };

        using var stream = new MemoryStream();
        var writeException = Assert.Throws<NotSupportedException>(() =>
            FitsSerializer.Write(stream, rows, AppFitsContext.Default.UnsupportedTableRow));

        Assert.Contains("does not support member", writeException.Message);
    }

    [Fact]
    public void GeneratedTableSerializerRoundTripsRowsAndColumns()
    {
        var rows = new[]
        {
            new EventRow { Time = 1.5, Channel = 4 },
            new EventRow { Time = 2.75, Channel = 9 },
        };

        using var stream = new MemoryStream();
        FitsSerializer.Write(stream, rows, AppFitsContext.Default.EventRow);
        stream.Position = 0;

        IReadOnlyList<EventRow> result = FitsSerializer.Read(stream, AppFitsContext.Default.EventRow);

        Assert.IsType<EventRow[]>(result);
        Assert.Equal(rows.Length, result.Count);
        for (int index = 0; index < rows.Length; index++)
        {
            Assert.Equal(rows[index].Time, result[index].Time);
            Assert.Equal(rows[index].Channel, result[index].Channel);
        }
    }

    [Theory]
    [InlineData(8_191)]
    [InlineData(8_192)]
    public void GeneratedTableSerializerRoundTripsAtBulkThresholdBoundary(int count)
    {
        var rows = Enumerable.Range(0, count)
            .Select(index => new EventRow { Time = index + 0.5, Channel = index })
            .ToArray();

        using var stream = new MemoryStream();
        FitsSerializer.Write(stream, rows, AppFitsContext.Default.EventRow);
        stream.Position = 0;

        IReadOnlyList<EventRow> result = FitsSerializer.Read(stream, AppFitsContext.Default.EventRow);

        Assert.Equal(rows.Length, result.Count);
        Assert.Equal(rows[0].Time, result[0].Time);
        Assert.Equal(rows[^1].Channel, result[^1].Channel);
    }

    [Fact]
    public void GeneratedTableSerializerUsesBulkCapableColumnsIndependently()
    {
        var rows = Enumerable.Range(0, 8_192)
            .Select(index => new MixedEventRow
            {
                Time = index + 0.5,
                Label = $"row-{index}",
                Channel = index,
            })
            .ToArray();

        using var stream = new MemoryStream();
        FitsSerializer.Write(stream, rows, AppFitsContext.Default.MixedEventRow);
        stream.Position = 0;

        IReadOnlyList<MixedEventRow> result = FitsSerializer.Read(stream, AppFitsContext.Default.MixedEventRow);

        Assert.Equal(rows.Length, result.Count);
        Assert.Equal(rows[0].Time, result[0].Time);
        Assert.Equal(rows[rows.Length - 1].Channel, result[rows.Length - 1].Channel);
        Assert.Equal(rows[rows.Length - 1].Label, result[rows.Length - 1].Label);
    }

    [Fact]
    public void GeneratedTableSerializerSelectsTheNamedExtension()
    {
        using var document = FitsDocumentBuilder.Create()
            .BinaryTable(
                new BinaryTableBuilder().Add("TIME", new[] { 99d }).Add("PHA", new[] { 99 }),
                header => header.Text("EXTNAME", "OTHER"))
            .BinaryTable(
                new BinaryTableBuilder().Add("TIME", new[] { 1.5d }).Add("PHA", new[] { 4 }),
                header => header.Text("EXTNAME", "EVENTS"))
            .Build();

        using var stream = new MemoryStream();
        document.Write(stream);
        stream.Position = 0;

        IReadOnlyList<EventRow> result = FitsSerializer.Read(stream, AppFitsContext.Default.EventRow);

        Assert.Single(result);
        Assert.Equal(1.5, result[0].Time);
        Assert.Equal(4, result[0].Channel);
    }

    [Fact]
    public void GeneratedDocumentReadsAllTypedHdusWithOneDocumentOpen()
    {
        using var document = FitsDocumentBuilder.Create()
            .Image([2, 2], new[] { 1f, 2f, 3f, 4f }, header =>
            {
                header.Real("EXPTIME", 12.5);
            })
            .BinaryTable(
                new BinaryTableBuilder().Add("TIME", new[] { 1.5 }).Add("PHA", new[] { 4 }),
                header => header.Text("EXTNAME", "EVENTS"))
            .Image([1], new byte[] { 9 }, header => header.Text("EXTNAME", "UNKNOWN"))
            .Build();

        using var stream = new MemoryStream(document.ToArray());
        MultiHduFile result = FitsSerializer.Read(stream, AppFitsContext.Default.MultiHduFile);

        Assert.Equal(12.5, result.Primary.ExposureSeconds);
        Assert.Equal(new[] { 1f, 2f, 3f, 4f }, result.Primary.Pixels!.Cast<float>());
        Assert.Single(result.Events);
        Assert.Equal(4, result.Events[0].Channel);
        Assert.Single(result.Unknown);
        Assert.Equal(2, result.Unknown[0].Index);
    }

    [Fact]
    public void GeneratedDocumentRoundTripsMappedAndUnknownHdus()
    {
        using var source = FitsDocumentBuilder.Create()
            .Image([2, 2], new[] { 1f, 2f, 3f, 4f }, header => header.Real("EXPTIME", 12.5))
            .BinaryTable(
                new BinaryTableBuilder().Add("TIME", new[] { 1.5 }).Add("PHA", new[] { 4 }),
                header => header.Text("EXTNAME", "EVENTS"))
            .Image([1], new byte[] { 9 }, header => header.Text("EXTNAME", "UNKNOWN"))
            .Build();
        MultiHduFile value;
        using (var input = new MemoryStream(source.ToArray()))
        {
            value = FitsSerializer.Read(input, AppFitsContext.Default.MultiHduFile);
        }

        value.Primary.ExposureSeconds = 25;
        value.Primary.Pixels![0, 0] = 8;
        value.Events = [new EventRow { Time = 3.5, Channel = 7 }];

        using var output = new MemoryStream();
        FitsSerializer.Write(output, value, AppFitsContext.Default.MultiHduFile);
        using var result = FitsDocument.Parse(output.ToArray());

        Assert.Equal(3, result.Hdus.Count);
        Assert.Equal(
            new[] { FitsHduKind.Image, FitsHduKind.BinaryTable, FitsHduKind.Image },
            result.Hdus.Select(hdu => hdu.Kind));
        Assert.Equal(new byte[] { 9 }, result.Hdus[2].RawData.ToArray());
        Assert.Equal(25, result.Hdus[0].Header.GetDouble("EXPTIME"));
        Assert.Equal(8, result.Hdus[0].Image!.ReadPhysical(0));
        Assert.Equal(7, result.Hdus[1].Table!.Column("PHA").ReadInteger(0));
    }

    [Fact]
    public void GeneratedDocumentPreservesInterleavedHduOrder()
    {
        using var source = FitsDocumentBuilder.Create()
            .Image([2, 2], new float[] { 1, 2, 3, 4 }, header => header.Real("EXPTIME", 1))
            .Image([1], new byte[] { 2 }, header => header.Text("EXTNAME", "UNKNOWN"))
            .BinaryTable(
                new BinaryTableBuilder().Add("TIME", new[] { 1.5 }).Add("PHA", new[] { 4 }),
                header => header.Text("EXTNAME", "EVENTS"))
            .Build();

        using var input = new MemoryStream(source.ToArray());
        InterleavedHduFile value = FitsSerializer.Read(
            input, AppFitsContext.Default.InterleavedHduFile);

        using var output = new MemoryStream();
        FitsSerializer.Write(output, value, AppFitsContext.Default.InterleavedHduFile);
        using var result = FitsDocument.Parse(output.ToArray());

        Assert.Equal(new[] { FitsHduKind.Image, FitsHduKind.Image, FitsHduKind.BinaryTable },
            result.Hdus.Select(hdu => hdu.Kind));
        Assert.Equal(new byte[] { 2 }, result.Hdus[1].RawData.ToArray());
    }

    [Fact]
    public void GeneratedDocumentRoundTripsSpecialRecords()
    {
        byte[] records = new byte[2880];
        "SPECIAL!"u8.CopyTo(records);
        using var source = FitsDocumentBuilder.Create()
            .Image([2, 2], new float[] { 1, 2, 3, 4 }, header => header.Real("EXPTIME", 1))
            .BinaryTable(
                new BinaryTableBuilder().Add("TIME", new[] { 1.5 }).Add("PHA", new[] { 4 }),
                header => header.Text("EXTNAME", "EVENTS"))
            .SpecialRecords(records)
            .Build();

        using var input = new MemoryStream(source.ToArray());
        MultiHduFile value = FitsSerializer.Read(input, AppFitsContext.Default.MultiHduFile);

        using var output = new MemoryStream();
        FitsSerializer.Write(output, value, AppFitsContext.Default.MultiHduFile);
        using var result = FitsDocument.Parse(output.ToArray());

        Assert.Equal(records, result.SnapshotSpecialRecords().ToArray());
    }

    [Fact]
    public void GeneratedCompositeDirectWriteMatchesStagedWrite()
    {
        var value = new DirectCompositeFile
        {
            Image = new Exposure
            {
                ExposureSeconds = 12.5,
                Pixels = new float[,] { { 1, 2 }, { 3, 4 } },
            },
            Events =
            [
                new EventRow { Time = 1.5, Channel = 4 },
                new EventRow { Time = 2.5, Channel = 7 },
            ],
        };

        using var generated = new MemoryStream();
        FitsSerializer.Write(generated, value, AppFitsContext.Default.DirectCompositeFile);

        using var staged = new MemoryStream();
        FitsDocumentBuilder.Create()
            .Image([2, 2], new[] { 1f, 2f, 3f, 4f }, header =>
            {
                header.Real("EXPTIME", value.Image.ExposureSeconds);
            })
            .BinaryTable(
                new BinaryTableBuilder()
                    .Add("TIME", value.Events.Select(row => row.Time).ToArray())
                    .Add("PHA", value.Events.Select(row => row.Channel).ToArray()),
                header => header.Text("EXTNAME", "EVENTS"))
            .Build()
            .Write(staged);

        using var generatedDocument = FitsDocument.Parse(generated.ToArray());
        using var stagedDocument = FitsDocument.Parse(staged.ToArray());
        Assert.Equal(
            stagedDocument.Hdus.Select(hdu => hdu.Kind),
            generatedDocument.Hdus.Select(hdu => hdu.Kind));
        Assert.Equal(
            stagedDocument.Hdus[1].Header.GetString("EXTNAME"),
            generatedDocument.Hdus[1].Header.GetString("EXTNAME"));
        Assert.Equal(
            stagedDocument.Hdus[1].Table!.Column("PHA").ReadInteger(1),
            generatedDocument.Hdus[1].Table!.Column("PHA").ReadInteger(1));
    }

    [Fact]
    public void GeneratedCompositeDirectWriteRoundTripsChildren()
    {
        var value = new DirectCompositeFile
        {
            Image = new Exposure { Pixels = new float[,] { { 1, 2 } } },
            Events = [new EventRow { Time = 3.5, Channel = 8 }],
        };

        using var stream = new MemoryStream();
        FitsSerializer.Write(stream, value, AppFitsContext.Default.DirectCompositeFile);
        stream.Position = 0;

        DirectCompositeFile result = FitsSerializer.Read(
            stream, AppFitsContext.Default.DirectCompositeFile);

        Assert.Equal(value.Image.Pixels, result.Image.Pixels);
        Assert.Equal(value.Events[0].Time, result.Events[0].Time);
        Assert.Equal(value.Events[0].Channel, result.Events[0].Channel);
    }
}
