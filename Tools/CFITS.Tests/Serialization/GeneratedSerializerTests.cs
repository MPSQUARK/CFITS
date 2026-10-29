using System.Buffers.Binary;
using System.Text;

namespace CFITS.Tests;

public sealed class GeneratedSerializerTests
{
    [Fact]
    public void GeneratedImageDirectWriteMatchesBuildWrite()
    {
        var value = new ExposureFile
        {
            Keywords = new ExposureKeywords { ExposureSeconds = 12.5 },
            Image = new ExposureImage
            {
                Pixels = new float[,]
                {
                    { 1.25f, 2.5f },
                    { 3.75f, 5f },
                },
            },
        };

        using var generatedStream = new MemoryStream();
        FitsSerializer.Write(generatedStream, value);

        using var builtStream = new MemoryStream();
        FitsDocumentBuilder.Create()
            .Image([2, 2], new[] { 1.25f, 2.5f, 3.75f, 5f }, header =>
            {
                header.Real("EXPTIME", value.Keywords.ExposureSeconds);
                header.Text("EXTNAME", "EXPOSURE");
            })
            .Build()
            .Write(builtStream);

        Assert.Equal(builtStream.ToArray(), generatedStream.ToArray());
        Assert.True(generatedStream.CanWrite);
    }

    [Fact]
    public void OpenReadsTheImageAndPrimaryKeywordsOnDemand()
    {
        var value = new ExposureFile
        {
            Keywords = new ExposureKeywords { ExposureSeconds = 12.5 },
            Image = new ExposureImage
            {
                Pixels = new float[,] { { 1.25f, 2.5f }, { 3.75f, 5f } },
            },
        };
        using var stream = new MemoryStream();
        FitsParser.Serialize(stream, value);
        stream.Position = 0;

        using ExposureFile.Reader reader = ExposureFile.Open(stream);
        Assert.Equal(12.5, reader.Keywords.ExposureSeconds);
        Assert.Equal(5f, reader.Image.Pixels![1, 1]);
        Assert.Equal(12.5, reader.Deserialize().Keywords.ExposureSeconds);
    }

    [Fact]
    public void GeneratedImageCanWriteIntoAnExistingBuilder()
    {
        var value = new ExposureFile
        {
            Keywords = new ExposureKeywords { ExposureSeconds = 12.5 },
            Image = new ExposureImage
            {
                Pixels = new float[,] { { 1, 2 }, { 3, 4 } },
            },
        };

        var builder = FitsDocumentBuilder.Create();
        FitsSerializer.Write(builder, value);

        using var stream = new MemoryStream();
        builder.Write(stream);
        using var document = FitsDocument.Parse(stream.ToArray());

        Assert.Equal(12.5, document.Hdus[0].Header.GetDouble("EXPTIME"));
        Assert.Equal(4, document.Hdus[0].Image!.ReadPhysical(3));
    }

    [Fact]
    public void GeneratedTableDirectWriteMatchesBuildWrite()
    {
        var rows = new[]
        {
            new EventRow { Time = 1.5, Channel = 4 },
            new EventRow { Time = 2.75, Channel = 9 },
        };
        var value = new EventFile { Events = new EventTable { Rows = rows } };

        using var generatedStream = new MemoryStream();
        FitsSerializer.Write(generatedStream, value);

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
        var value = new ExposureFile
        {
            Keywords = new ExposureKeywords { ExposureSeconds = 12.5 },
            Image = new ExposureImage
            {
                Pixels = new float[,]
                {
                    { 1.25f, 2.5f },
                    { 3.75f, 5f },
                },
            },
        };

        using var stream = new MemoryStream();
        FitsSerializer.Write(stream, value);
        stream.Position = 0;

        ExposureFile result = FitsSerializer.Read<ExposureFile>(stream);

        Assert.Equal(value.Keywords.ExposureSeconds, result.Keywords.ExposureSeconds);
        Assert.NotNull(result.Image.Pixels);
        Assert.Equal(value.Image.Pixels!.GetLength(0), result.Image.Pixels.GetLength(0));
        Assert.Equal(value.Image.Pixels.GetLength(1), result.Image.Pixels.GetLength(1));
        for (int row = 0; row < value.Image.Pixels.GetLength(0); row++)
        {
            for (int column = 0; column < value.Image.Pixels.GetLength(1); column++)
            {
                Assert.Equal(value.Image.Pixels[row, column], result.Image.Pixels[row, column]);
            }
        }
    }

    [Fact]
    public void GeneratedImageSerializerRoundTripsByteKeywordsAsIntegers()
    {
        var value = new ByteKeywordFile
        {
            Keywords = new ByteKeywords { BitDepth = 255 },
            Image = new ByteKeywordImage { Pixels = [1, 2, 3] },
        };

        using var stream = new MemoryStream();
        FitsSerializer.Write(stream, value);
        stream.Position = 0;

        using var document = FitsDocument.Parse(stream.ToArray());
        Assert.Equal(255, document.Hdus[0].Header.GetInt64("LEVEL"));

        stream.Position = 0;
        ByteKeywordFile result = FitsSerializer.Read<ByteKeywordFile>(stream);

        Assert.Equal(value.Keywords.BitDepth, result.Keywords.BitDepth);
        Assert.Equal(value.Image.Pixels, result.Image.Pixels);
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
            FitsSerializer.Read<ExposureFile>(
                stream,
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
            FitsSerializer.Read<ExposureFile>(stream));

        Assert.Contains("rank", exception.Message);
    }

    [Fact]
    public void GeneratedIntegerImageSerializerPreservesLongValuesBeyondDoublePrecision()
    {
        long[] pixels = [9_007_199_254_740_993, -9_007_199_254_740_993];
        var value = new LongExposureFile
        {
            Image = new LongExposureImage { Pixels = pixels },
        };

        using var stream = new MemoryStream();
        FitsSerializer.Write(stream, value);
        stream.Position = 0;

        LongExposureFile result = FitsSerializer.Read<LongExposureFile>(stream);

        Assert.Equal(pixels, result.Image.Pixels);
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
        FitsSerializer.Write(stream, new EventFile { Events = new EventTable { Rows = rows } });
        stream.Position = 0;

        IReadOnlyList<EventRow> result = FitsSerializer.Read<EventFile>(stream).Events.Rows;

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
        FitsSerializer.Write(stream, new EventFile { Events = new EventTable { Rows = rows } });
        stream.Position = 0;

        IReadOnlyList<EventRow> result = FitsSerializer.Read<EventFile>(stream).Events.Rows;

        Assert.Equal(rows.Length, result.Count);
        Assert.Equal(rows[0].Time, result[0].Time);
        Assert.Equal(rows[^1].Channel, result[^1].Channel);
    }

    [Fact]
    public void GeneratedStructRowsRoundTripWithoutPerRowObjects()
    {
        var rows = new StructEventRow[]
        {
            new() { Time = 1.5, Channel = 4 },
            new() { Time = 2.75, Channel = 9 },
        };

        using var stream = new MemoryStream();
        FitsSerializer.Write(stream, new StructEventFile { Events = new StructEventTable { Rows = rows } });
        stream.Position = 0;
        IReadOnlyList<StructEventRow> result = FitsSerializer.Read<StructEventFile>(stream).Events.Rows;

        Assert.IsType<StructEventRow[]>(result);
        Assert.Equal(2, result.Count);
        Assert.Equal(1.5, result[0].Time);
        Assert.Equal(9, result[1].Channel);
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
        FitsSerializer.Write(stream, new MixedEventFile { Events = new MixedEventTable { Rows = rows } });
        stream.Position = 0;

        IReadOnlyList<MixedEventRow> result = FitsSerializer.Read<MixedEventFile>(stream).Events.Rows;

        Assert.Equal(rows.Length, result.Count);
        Assert.Equal(rows[0].Time, result[0].Time);
        Assert.Equal(rows[rows.Length - 1].Channel, result[rows.Length - 1].Channel);
        Assert.Equal(rows[rows.Length - 1].Label, result[rows.Length - 1].Label);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedVariableDoubleReadMatchesRuntimeForPAndQDescriptors(bool useQDescriptor)
    {
        using var document = CreateVariableDoubleDocument(useQDescriptor);
        byte[] bytes = NamedExtension(document, "Table");

        IReadOnlyList<VariableDoubleRow> generated;
        using (var generatedStream = new MemoryStream(bytes))
        {
            generated = FitsSerializer.Read<VariableDoubleFile>(generatedStream).Table.Rows;
        }

        using var runtimeDocument = FitsDocument.Parse(bytes);
        FitsColumn column = runtimeDocument.Hdus[1].Table!.Column("VALUES");
        Assert.Equal(2, generated.Count);
        for (int row = 0; row < generated.Count; row++)
        {
            Assert.Equal(column.ReadRealVector(row), generated[row].Values);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedVariableDoubleReadRejectsMalformedHeapLikeRuntime(bool useQDescriptor)
    {
        using var document = CreateVariableDoubleDocument(useQDescriptor);
        byte[] bytes = NamedExtension(document, "Table");
        int tableDataOffset = 5_760;
        int descriptorOffset = tableDataOffset + (useQDescriptor ? 8 : 4);
        BinaryPrimitives.WriteInt64BigEndian(
            bytes.AsSpan(descriptorOffset, 8),
            9);
        if (!useQDescriptor)
        {
            BinaryPrimitives.WriteInt32BigEndian(
                bytes.AsSpan(descriptorOffset, 4),
                9);
        }

        using var generatedStream = new MemoryStream(bytes);
        Assert.Throws<FitsException>(() =>
            FitsSerializer.Read<VariableDoubleFile>(generatedStream));

        Assert.Throws<FitsException>(() => FitsDocument.Parse(bytes));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedVariableIntReadMatchesRuntimeForPAndQDescriptors(bool useQDescriptor)
    {
        using var document = CreateVariableIntDocument(useQDescriptor);
        byte[] bytes = NamedExtension(document, "Table");

        IReadOnlyList<VariableIntRow> generated;
        using (var generatedStream = new MemoryStream(bytes))
        {
            generated = FitsSerializer.Read<VariableIntFile>(generatedStream).Table.Rows;
        }

        using var runtimeDocument = FitsDocument.Parse(bytes);
        FitsColumn column = runtimeDocument.Hdus[1].Table!.Column("VALUES");
        Assert.Equal(2, generated.Count);
        for (int row = 0; row < generated.Count; row++)
        {
            Assert.Equal(column.ReadIntegerVector(row).Select(static value => (int)value), generated[row].Values);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedVariableIntReadRejectsMalformedHeapLikeRuntime(bool useQDescriptor)
    {
        using var document = CreateVariableIntDocument(useQDescriptor);
        byte[] bytes = NamedExtension(document, "Table");
        int tableDataOffset = 5_760;
        int descriptorOffset = tableDataOffset + (useQDescriptor ? 8 : 4);
        if (useQDescriptor)
        {
            BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(descriptorOffset, 8), 9);
        }
        else
        {
            BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(descriptorOffset, 4), 9);
        }

        using var generatedStream = new MemoryStream(bytes);
        Assert.Throws<FitsException>(() =>
            FitsSerializer.Read<VariableIntFile>(generatedStream));

        Assert.Throws<FitsException>(() => FitsDocument.Parse(bytes));
    }

    private static FitsDocument CreateVariableDoubleDocument(bool useQDescriptor)
    {
        const string format = "1PD";
        byte[] heap = new byte[16];
        BinaryPrimitives.WriteInt64BigEndian(heap, BitConverter.DoubleToInt64Bits(1.5));
        BinaryPrimitives.WriteInt64BigEndian(heap.AsSpan(8), BitConverter.DoubleToInt64Bits(2.5));
        byte[] row = new byte[useQDescriptor ? 16 : 8];
        if (useQDescriptor)
        {
            BinaryPrimitives.WriteInt64BigEndian(row, 2);
            BinaryPrimitives.WriteInt64BigEndian(row.AsSpan(8), 0);
        }
        else
        {
            BinaryPrimitives.WriteInt32BigEndian(row, 2);
            BinaryPrimitives.WriteInt32BigEndian(row.AsSpan(4), 0);
        }

        var builder = new FitsRawTableBuilder(false)
            .AddColumn("VALUES", useQDescriptor ? "1QD" : format)
            .AddRow(row)
            .SetHeap(heap);
        builder.AddRow(useQDescriptor ? new byte[16] : new byte[8]);

        return builder.Build();
    }

    private static FitsDocument CreateVariableIntDocument(bool useQDescriptor)
    {
        byte[] heap = new byte[8];
        BinaryPrimitives.WriteInt32BigEndian(heap.AsSpan(0, 4), 15);
        BinaryPrimitives.WriteInt32BigEndian(heap.AsSpan(4, 4), 25);
        byte[] row = new byte[useQDescriptor ? 16 : 8];
        if (useQDescriptor)
        {
            BinaryPrimitives.WriteInt64BigEndian(row, 2);
            BinaryPrimitives.WriteInt64BigEndian(row.AsSpan(8), 0);
        }
        else
        {
            BinaryPrimitives.WriteInt32BigEndian(row, 2);
            BinaryPrimitives.WriteInt32BigEndian(row.AsSpan(4), 0);
        }

        var builder = new FitsRawTableBuilder(false)
            .AddColumn("VALUES", useQDescriptor ? "1QJ" : "1PJ")
            .AddRow(row)
            .SetHeap(heap);
        builder.AddRow(useQDescriptor ? new byte[16] : new byte[8]);

        return builder.Build();
    }

    private static byte[] NamedExtension(FitsDocument document, string name)
    {
        byte[] bytes = document.ToArray();
        int header = 2_880;
        for (int card = header; card + 160 <= bytes.Length; card += 80)
        {
            if (bytes[card] != (byte)'E'
                || bytes[card + 1] != (byte)'N'
                || bytes[card + 2] != (byte)'D'
                || bytes[card + 3] != (byte)' ')
            {
                continue;
            }

            byte[] extname = Encoding.ASCII.GetBytes($"EXTNAME = '{name}'");
            for (int index = 0; index < 80; index++)
            {
                bytes[card + index] = index < extname.Length ? extname[index] : (byte)' ';
            }

            byte[] end = "END"u8.ToArray();
            for (int index = 0; index < 80; index++)
            {
                bytes[card + 80 + index] = index < end.Length ? end[index] : (byte)' ';
            }

            return bytes;
        }

        throw new InvalidOperationException("The extension END card was not found.");
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

        IReadOnlyList<EventRow> result = FitsSerializer.Read<NamedEventFile>(stream).Events.Rows;

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
        MultiHduFile result = FitsSerializer.Read<MultiHduFile>(stream);

        Assert.Equal(12.5, result.Keywords.ExposureSeconds);
        Assert.Equal(new[] { 1f, 2f, 3f, 4f }, result.Image.Pixels!.Cast<float>());
        Assert.Single(result.Events.Rows);
        Assert.Equal(4, result.Events.Rows[0].Channel);
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
            value = FitsSerializer.Read<MultiHduFile>(input);
        }

        value.Keywords.ExposureSeconds = 25;
        value.Image.Pixels![0, 0] = 8;
        value.Events.Rows = [new EventRow { Time = 3.5, Channel = 7 }];

        using var output = new MemoryStream();
        FitsSerializer.Write(output, value);
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
        InterleavedHduFile value = FitsSerializer.Read<InterleavedHduFile>(input);

        using var output = new MemoryStream();
        FitsSerializer.Write(output, value);
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
        MultiHduFile value = FitsSerializer.Read<MultiHduFile>(input);

        using var output = new MemoryStream();
        FitsSerializer.Write(output, value);
        using var result = FitsDocument.Parse(output.ToArray());

        Assert.Equal(records, result.SnapshotSpecialRecords().ToArray());
    }

    [Fact]
    public void GeneratedCompositeDirectWriteMatchesStagedWrite()
    {
        var value = new DirectCompositeFile
        {
            Keywords = new ExposureKeywords { ExposureSeconds = 12.5 },
            Image = new ExposureImage
            {
                Pixels = new float[,] { { 1, 2 }, { 3, 4 } },
            },
            Events = new EventTable
            {
                Rows =
                [
                    new EventRow { Time = 1.5, Channel = 4 },
                    new EventRow { Time = 2.5, Channel = 7 },
                ],
            },
        };

        using var generated = new MemoryStream();
        FitsSerializer.Write(generated, value);

        using var staged = new MemoryStream();
        FitsDocumentBuilder.Create()
            .Image([2, 2], new[] { 1f, 2f, 3f, 4f }, header =>
            {
                header.Real("EXPTIME", value.Keywords.ExposureSeconds);
            })
            .BinaryTable(
                new BinaryTableBuilder()
                    .Add("TIME", value.Events.Rows.Select(row => row.Time).ToArray())
                    .Add("PHA", value.Events.Rows.Select(row => row.Channel).ToArray()),
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
            Keywords = new ExposureKeywords(),
            Image = new ExposureImage { Pixels = new float[,] { { 1, 2 } } },
            Events = new EventTable { Rows = [new EventRow { Time = 3.5, Channel = 8 }] },
        };

        using var stream = new MemoryStream();
        FitsSerializer.Write(stream, value);
        stream.Position = 0;

        DirectCompositeFile result = FitsSerializer.Read<DirectCompositeFile>(stream);

        Assert.Equal(value.Image.Pixels, result.Image.Pixels);
        Assert.Equal(value.Events.Rows[0].Time, result.Events.Rows[0].Time);
        Assert.Equal(value.Events.Rows[0].Channel, result.Events.Rows[0].Channel);
    }

    [Fact]
    public void GeneratedDocumentCanWriteIntoAnExistingBuilder()
    {
        var value = new DirectCompositeFile
        {
            Keywords = new ExposureKeywords { ExposureSeconds = 8.5 },
            Image = new ExposureImage
            {
                Pixels = new float[,] { { 1, 2 }, { 3, 4 } },
            },
            Events = new EventTable { Rows = [new EventRow { Time = 6.25, Channel = 11 }] },
        };

        using var direct = new MemoryStream();
        FitsSerializer.Write(direct, value);

        var builder = FitsDocumentBuilder.Create();
        FitsSerializer.Write(builder, value);
        using var composed = new MemoryStream();
        builder.Write(composed);

        Assert.Equal(direct.ToArray(), composed.ToArray());
    }
}
