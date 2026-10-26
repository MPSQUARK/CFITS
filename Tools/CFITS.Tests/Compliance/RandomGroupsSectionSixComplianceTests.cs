using System.Buffers.Binary;
using System.Text;

namespace CFITS.Tests;

public sealed class RandomGroupsSectionSixComplianceTests
{
    [Fact]
    public void AppliesBlankAndIndependentScalingToParametersAndData()
    {
        byte[] file = RandomGroupsFile(
            bitpix: 16,
            axes: [2],
            pcount: 1,
            gcount: 1,
            parameterCards: ["PTYPE1  = 'TIME'           ", "PSCAL1  =                  2", "PZERO1  =                100"],
            extraCards: ["BSCALE  =                  3", "BZERO   =                 10", "BLANK   =                -99"],
            values: [4, -99, 5]);

        using FitsDocument document = FitsDocument.Parse(file);
        FitsGroups groups = document.Hdus[0].Groups!;

        Assert.Equal(108, groups.ReadParameter(0, 0));
        Assert.True(double.IsNaN(groups.ReadData(0, 0)));
        Assert.Equal(25, groups.ReadData(0, 1));
    }

    [Fact]
    public void SumsRepeatedParameterNamesAsIndependentPhysicalAddends()
    {
        using FitsDocument document = FitsDocumentBuilder.Create()
            .RandomGroups(
                bitpix: 16,
                dataAxes: [1],
                parameterCount: 2,
                parameters: [100, 3],
                data: [7],
                parameterNames: ["TIME", "TIME"],
                header: header => header
                    .Real("PSCAL1", 0.01)
                    .Real("PZERO1", 1000)
                    .Real("PZERO2", -3))
            .Build();

        FitsGroups groups = document.Hdus[0].Groups!;

        Assert.Equal(1001, groups.ReadParameter(0, "TIME"), 12);
        Assert.Equal(1001, groups.ReadParameter(0, 0), 12);
        Assert.Equal(0, groups.ReadParameter(0, 1), 12);
        Assert.Throws<FitsException>(() => groups.ReadParameter(0, "MISSING"));
    }

    [Fact]
    public void RejectsKeywordsAndAxesInterleavedWithOrBeyondMandatoryRandomGroupsStructure()
    {
        byte[] interleaved = Header(
            "SIMPLE  =                    T",
            "BITPIX  =                   16",
            "FOO     =                    1",
            "NAXIS   =                    2",
            "NAXIS1  =                    0",
            "NAXIS2  =                    1",
            "GROUPS  =                    T",
            "PCOUNT  =                    0",
            "GCOUNT  =                    0");
        Assert.Contains("before the last NAXISn", Assert.Throws<FitsException>(() => FitsDocument.Parse(interleaved)).Message);

        byte[] extraAxis = Header(
            "SIMPLE  =                    T",
            "BITPIX  =                   16",
            "NAXIS   =                    2",
            "NAXIS1  =                    0",
            "NAXIS2  =                    1",
            "NAXIS3  =                    1",
            "GROUPS  =                    T",
            "PCOUNT  =                    0",
            "GCOUNT  =                    0");
        Assert.Contains("NAXIS3", Assert.Throws<FitsException>(() => FitsDocument.Parse(extraAxis)).Message);
    }

    [Fact]
    public void RejectsNonzeroRandomGroupsDataPadding()
    {
        byte[] file = RandomGroupsFile(16, [1], 0, 1, [], [], [7]);
        file[^1] = 1;

        Assert.Contains("padding", Assert.Throws<FitsException>(() => FitsDocument.Parse(file)).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PreservesSectionSixScalingAndSequenceAcrossDeterministicGeneratedCases()
    {
        foreach (int bitpix in new[] { 8, 16, 32, 64, -32, -64 })
        {
            const int groupsCount = 3;
            const int parameters = 2;
            double[] parameterValues = Enumerable.Range(0, groupsCount * parameters)
                .Select(index => (double)(index + 1))
                .ToArray();
            double[] dataValues = Enumerable.Range(0, groupsCount * 4)
                .Select(index => (double)(index * 2 + 3))
                .ToArray();

            using FitsDocument document = FitsDocumentBuilder.Create()
                .RandomGroups(
                    bitpix,
                    [2, 2],
                    parameters,
                    parameterValues,
                    dataValues,
                    ["A", "B"],
                    header => header
                        .Real("PSCAL1", 2.5)
                        .Real("PZERO1", -10)
                        .Real("PSCAL2", -0.5)
                        .Real("PZERO2", 4))
                .Build();

            FitsGroups groups = document.Hdus[0].Groups!;
            for (int group = 0; group < groupsCount; group++)
            {
                Assert.Equal(-10 + 2.5 * parameterValues[group * parameters], groups.ReadParameter(group, 0), 10);
                Assert.Equal(4 - 0.5 * parameterValues[(group * parameters) + 1], groups.ReadParameter(group, 1), 10);
                for (int index = 0; index < 4; index++)
                {
                    Assert.Equal(dataValues[(group * 4) + index], groups.ReadData(group, index), 10);
                }
            }
        }
    }

    [Fact]
    public void AcceptsZeroGroupsWithAndWithoutParameters()
    {
        using FitsDocument noParameters = FitsDocument.Parse(
            RandomGroupsFile(16, [2], 0, 0, [], [], []));
        using FitsDocument parameters = FitsDocument.Parse(
            RandomGroupsFile(16, [2], 2, 0, ["PTYPE1  = 'A'", "PTYPE2  = 'B'"], [], []));

        Assert.Equal(0, noParameters.Hdus[0].Groups!.Count);
        Assert.Equal(0, noParameters.Hdus[0].Groups!.ParameterCount);
        Assert.Equal(0, parameters.Hdus[0].Groups!.Count);
        Assert.Equal(2, parameters.Hdus[0].Groups!.ParameterCount);
    }

    [Fact]
    public void RejectsZeroDataAxisInRandomGroups()
    {
        Assert.Contains("NAXIS2", Assert.Throws<FitsException>(() =>
            FitsDocument.Parse(RandomGroupsFile(16, [0], 0, 0, [], [], []))).Message);
    }

    [Fact]
    public void RejectsParameterKeywordsBeyondPcount()
    {
        foreach (string keyword in new[] { "PTYPE2", "PSCAL2", "PZERO2" })
        {
            Assert.Contains("parameter keyword 2", Assert.Throws<FitsException>(() =>
                FitsDocument.Parse(RandomGroupsFile(16, [1], 1, 0, new[] { keyword.PadRight(8) + "= 1" }, Array.Empty<string>(), Array.Empty<double>()))).Message);
        }
    }

    [Fact]
    public void RejectsRandomGroupsKeywordOrderAndTypes()
    {
        Assert.Throws<FitsException>(() => FitsDocument.Parse(
            RandomGroupsFile(16, [1], 0, 0, [], ["GCOUNT  =                    0", "GROUPS  =                    T"], [])));
        Assert.Throws<FitsException>(() => FitsDocument.Parse(
            RandomGroupsFile(16, [1], 0, 0, [], ["PCOUNT  = '0'"], [])));
        Assert.Throws<FitsException>(() => FitsDocument.Parse(
            RandomGroupsFile(16, [1], 0, 0, [], ["GROUPS  =                    1"], [])));
    }

    [Fact]
    public void RejectsRandomGroupsPayloadThatDoesNotMatchTheDeclaredSequence()
    {
        byte[] file = RandomGroupsFile(16, new[] { 2 }, 1, 1, Array.Empty<string>(), Array.Empty<string>(), new[] { 7d, 11d })[..(2880 + 4)];
        Assert.Contains("data array", Assert.Throws<FitsException>(() => FitsDocument.Parse(file)).Message);
    }

    [Fact]
    public void RejectsOverflowingRandomGroupsShapeBeforeAllocating()
    {
        byte[] file = Header(
            "SIMPLE  =                    T",
            "BITPIX  =                   64",
            "NAXIS   =                    3",
            "NAXIS1  =                    0",
            "NAXIS2  =           2147483647",
            "NAXIS3  =           2147483647",
            "GROUPS  =                    T",
            "PCOUNT  =           2147483647",
            "GCOUNT  =           2147483647");

        FitsException error = Assert.Throws<FitsException>(() => FitsDocument.Parse(file));
        Assert.Contains("exceed", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WritesAndReadsEveryLegalIntegerAndFloatingBitpix()
    {
        foreach (int bitpix in new[] { 8, 16, 32, 64, -32, -64 })
        {
            using FitsDocument document = FitsDocumentBuilder.Create()
                .RandomGroups(bitpix, new[] { 2 }, 1, new[] { 7d }, new[] { 11d, 13d })
                .Build();
            FitsGroups groups = document.Hdus[0].Groups!;
            Assert.Equal(7, groups.ReadParameter(0, 0));
            Assert.Equal(11, groups.ReadData(0, 0));
            Assert.Equal(13, groups.ReadData(0, 1));
        }
    }

    private static byte[] RandomGroupsFile(
        int bitpix,
        int[] axes,
        int pcount,
        int gcount,
        string[] parameterCards,
        string[] extraCards,
        double[] values)
    {
        var cards = new List<string>
        {
            "SIMPLE  =                    T",
            $"BITPIX  = {bitpix,20}",
            $"NAXIS   = {axes.Length + 1,20}",
            "NAXIS1  =                    0",
        };
        for (int index = 0; index < axes.Length; index++)
        {
            cards.Add($"NAXIS{index + 2}".PadRight(8) + $"= {axes[index],20}");
        }

        cards.Add("GROUPS  =                    T");
        cards.Add($"PCOUNT  = {pcount,20}");
        cards.Add($"GCOUNT  = {gcount,20}");
        cards.AddRange(parameterCards);
        cards.AddRange(extraCards);

        int width = Math.Abs(bitpix) / 8;
        byte[] data = new byte[values.Length * width];
        for (int index = 0; index < values.Length; index++)
        {
            switch (bitpix)
            {
                case 8: data[index] = (byte)values[index]; break;
                case 16: BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(index * width, width), (short)values[index]); break;
                case 32: BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(index * width, width), (int)values[index]); break;
                case 64: BinaryPrimitives.WriteInt64BigEndian(data.AsSpan(index * width, width), (long)values[index]); break;
                case -32: BinaryPrimitives.WriteSingleBigEndian(data.AsSpan(index * width, width), (float)values[index]); break;
                case -64: BinaryPrimitives.WriteDoubleBigEndian(data.AsSpan(index * width, width), values[index]); break;
            }
        }

        const int blockSize = 2880;
        int dataPadding = (blockSize - (data.Length % blockSize)) % blockSize;
        return Header(cards.ToArray()).Concat(data).Concat(new byte[dataPadding]).ToArray();
    }

    private static byte[] Header(params string[] cards)
    {
        byte[] header = Encoding.ASCII.GetBytes(string.Concat(cards.Select(card => card.PadRight(80))));
        header = header.Concat(Encoding.ASCII.GetBytes("END".PadRight(80))).ToArray();
        const int blockSize = 2880;
        int padding = (blockSize - (header.Length % blockSize)) % blockSize;
        return header.Concat(new byte[padding]).ToArray();
    }
}
