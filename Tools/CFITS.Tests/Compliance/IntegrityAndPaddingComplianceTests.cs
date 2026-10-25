using System.Text;

namespace CFITS.Tests;

public sealed class IntegrityAndPaddingComplianceTests
{
    [Fact]
    public void ReportsEachChecksumKeywordIndependently()
    {
        byte[] data = Enumerable.Repeat((byte)' ', 2880).ToArray();
        byte[] onlyDataSum = HeaderWith(data, "DATASUM", "0");
        byte[] onlyChecksum = HeaderWith(data, "CHECKSUM", "0000000000000000");

        using var dataSumDocument = FitsDocument.Parse(onlyDataSum);
        using var checksumDocument = FitsDocument.Parse(onlyChecksum);

        Assert.Null(dataSumDocument.Hdus[0].Integrity.ChecksumMatches);
        Assert.False(dataSumDocument.Hdus[0].Integrity.DataSumMatches);
        Assert.False(checksumDocument.Hdus[0].Integrity.ChecksumMatches);
        Assert.Null(checksumDocument.Hdus[0].Integrity.DataSumMatches);
        Assert.Throws<FitsException>(() => FitsDocument.Parse(onlyDataSum, new FitsOpenOptions { VerifyChecksum = true }));
        Assert.Throws<FitsException>(() => FitsDocument.Parse(onlyChecksum, new FitsOpenOptions { VerifyChecksum = true }));
    }

    [Fact]
    public void RejectsInvalidChecksumCombinationsAndMalformedNumericDataSums()
    {
        using var document = FitsDocumentBuilder.Create()
            .Image([2], new short[] { 1, 2 })
            .Checksum()
            .Build();
        byte[] malformedDataSum = document.ToArray();
        ReplaceCard(malformedDataSum, "DATASUM", "NOT_A_NUMBER");
        Assert.Null(FitsDocument.Parse(malformedDataSum).Hdus[0].Integrity.DataSumMatches);

        byte[] malformedChecksum = document.ToArray();
        ReplaceCard(malformedChecksum, "CHECKSUM", "NOT_A_CHECKSUM");
        Assert.False(FitsDocument.Parse(malformedChecksum).Hdus[0].Integrity.ChecksumMatches);
        Assert.Throws<FitsException>(() => FitsDocument.Parse(malformedChecksum, new FitsOpenOptions { VerifyChecksum = true }));
    }

    [Fact]
    public void IncludesDataPaddingInIntegrityVerification()
    {
        using var document = FitsDocumentBuilder.Create()
            .Image([1], new byte[] { 42 })
            .Checksum()
            .Build();
        byte[] bytes = document.ToArray();
        int lastPaddingByte = bytes.Length - 1;
        byte original = bytes[lastPaddingByte];
        bytes[lastPaddingByte] = (byte)(original ^ 0x01);

        using var corrupted = FitsDocument.Parse(bytes);
        Assert.False(corrupted.Hdus[0].Integrity.IsValid);
        Assert.Throws<FitsException>(() => FitsDocument.Parse(bytes, new FitsOpenOptions { VerifyChecksum = true }));
    }

    [Fact]
    public void RejectsDuplicateIntegrityKeywords()
    {
        using var document = FitsDocumentBuilder.Create()
            .Image([1], new byte[] { 42 }, header => header
                .Integer("DATASUM", 0)
                .Text("CHECKSUM", "0000000000000000")
                .Integer("DATASUM", 0)
                .Text("CHECKSUM", "0000000000000000"))
            .Checksum()
            .Build();

        Assert.False(document.Hdus[0].Integrity.ChecksumMatches);
        Assert.False(document.Hdus[0].Integrity.DataSumMatches);
        Assert.False(document.Hdus[0].Integrity.IsValid);
        Assert.Throws<FitsException>(() => FitsDocument.Parse(document.ToArray(), new FitsOpenOptions { VerifyChecksum = true }));
    }

    [Fact]
    public void DetectsTamperedHeaderSeparatelyFromData()
    {
        using var document = FitsDocumentBuilder.Create()
            .Image([1], new byte[] { 42 })
            .Checksum()
            .Build();
        byte[] bytes = document.ToArray();
        int checksumCard = bytes.AsSpan().IndexOf("CHECKSUM"u8);
        Assert.True(checksumCard >= 0);
        bytes[checksumCard + 11] = bytes[checksumCard + 11] == (byte)'0' ? (byte)'1' : (byte)'0';

        using var corrupted = FitsDocument.Parse(bytes);
        Assert.False(corrupted.Hdus[0].Integrity.ChecksumMatches);
        Assert.True(corrupted.Hdus[0].Integrity.DataSumMatches);
        Assert.Throws<FitsException>(() => FitsDocument.Parse(bytes, new FitsOpenOptions { VerifyChecksum = true }));
    }

    private static byte[] HeaderWith(byte[] data, string keyword, string value)
    {
        var builder = new FitsFileBuilder()
            .Card("SIMPLE", "T")
            .Card("BITPIX", "8")
            .Card("NAXIS", "1")
            .Card("NAXIS1", "2880");
        if (keyword == "CHECKSUM")
        {
            builder.Text(keyword, value);
        }
        else
        {
            builder.Card(keyword, value);
        }

        return builder.Build().Concat(data).ToArray();
    }

    private static void ReplaceCard(byte[] bytes, string keyword, string value)
    {
        for (int offset = 0; offset < 2880; offset += 80)
        {
            if (Encoding.ASCII.GetString(bytes, offset, 8).Trim() == keyword)
            {
                string card = keyword.PadRight(8) + "= '" + value + "'";
                Encoding.ASCII.GetBytes(card.PadRight(80)).CopyTo(bytes.AsSpan(offset));
                return;
            }
        }

        throw new InvalidOperationException($"Card {keyword} was not found.");
    }
}
