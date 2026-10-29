using System.Diagnostics;
using System.ComponentModel;
using Xunit.Sdk;

namespace CFITS.Tests;

public sealed class DocumentSpecTests
{
    [Fact]
    public void DocumentSpecPreservesInterleavedMappedAndMultipleUnknownHdus()
    {
        using var source = FitsDocumentBuilder.Create()
            .Image([1, 1], new float[] { 1.5f }, header => header.Real("EXPTIME", 1.5))
            .Image([1], new byte[] { 7 }, header => header.Text("EXTNAME", "UNKNOWN_1"))
            .BinaryTable(
                new BinaryTableBuilder().Add("TIME", new[] { 2.5 }).Add("PHA", new[] { 8 }),
                header => header.Text("EXTNAME", "EVENTS"))
            .Image([1], new byte[] { 9 }, header => header.Text("EXTNAME", "UNKNOWN_2"))
            .Build();

        using var input = new MemoryStream(source.ToArray());
        InterleavedHduFile value = FitsSerializer.Read<InterleavedHduFile>(input);

        Assert.Equal(1.5, value.Keywords.ExposureSeconds);
        Assert.Single(value.Events.Rows);
        Assert.Equal(8, value.Events.Rows[0].Channel);
        Assert.Equal(2, value.Unknown.Count);
        Assert.Equal(
            new byte[] { 7, 9 },
            value.Unknown.Select(snapshot => snapshot.DataBytes.Span[0]).ToArray());

        using var output = new MemoryStream();
        FitsSerializer.Write(output, value);
        using var roundTrip = FitsDocument.Parse(output.ToArray());

        Assert.Equal(
            new[] { FitsHduKind.Image, FitsHduKind.Image, FitsHduKind.BinaryTable, FitsHduKind.Image },
            roundTrip.Hdus.Select(hdu => hdu.Kind));
        Assert.Equal(
            new byte[] { 7, 9 },
            roundTrip.Hdus
                .Where(hdu => hdu.Kind == FitsHduKind.Image)
                .Skip(1)
                .Select(hdu => (byte)hdu.Image!.ReadInteger(0))
                .ToArray());
    }

    [Fact]
    public void DocumentSpecMapsGeneratedImageAndTableAndRetainsSpecialRecords()
    {
        byte[] special = new byte[2880];
        "SPEC-RECORDS"u8.CopyTo(special);
        using var source = FitsDocumentBuilder.Create()
            .Image([2, 1], new[] { 3.25f, 4.5f }, header => header.Real("EXPTIME", 6.25))
            .BinaryTable(
                new BinaryTableBuilder().Add("TIME", new[] { 10.5, 11.5 }).Add("PHA", new[] { 2, 4 }),
                header => header.Text("EXTNAME", "EVENTS"))
            .SpecialRecords(special)
            .Build();

        using var input = new MemoryStream(source.ToArray());
        MultiHduFile value = FitsSerializer.Read<MultiHduFile>(input);

        Assert.Equal(6.25, value.Keywords.ExposureSeconds);
        Assert.Equal(new[] { 3.25f, 4.5f }, value.Image.Pixels!.Cast<float>());
        Assert.Equal(new[] { 2, 4 }, value.Events.Rows.Select(row => row.Channel));
        Assert.Equal(special, value.SpecialRecords.ToArray());

        using var output = new MemoryStream();
        FitsSerializer.Write(output, value);
        using var result = FitsDocument.Parse(output.ToArray());

        Assert.Equal(special, result.SnapshotSpecialRecords().ToArray());
        Assert.Equal("EVENTS", result.Hdus[1].Header.GetString("EXTNAME"));
        Assert.Equal(4, result.Hdus[1].Table!.Column("PHA").ReadInteger(1));
    }

    [Fact]
    public void DocumentSpecChecksumValidityIsIndependentPerHdu()
    {
        using var document = FitsDocumentBuilder.Create()
            .Image([2], new short[] { 1, 2 })
            .BinaryTable(
                new BinaryTableBuilder().Add("PHA", new[] { 3, 4 }),
                header => header.Text("EXTNAME", "EVENTS"))
            .Checksum()
            .Build();

        Assert.All(document.Hdus, hdu =>
        {
            Assert.True(hdu.Integrity.ChecksumMatches);
            Assert.True(hdu.Integrity.DataSumMatches);
            Assert.True(hdu.Integrity.IsValid);
            Assert.Contains("CHECKSUM", hdu.Header.Cards.Select(card => card.Keyword));
            Assert.Contains("DATASUM", hdu.Header.Cards.Select(card => card.Keyword));
        });

        byte[] corrupted = document.ToArray();
        corrupted[2880] ^= 0x01;
        using var parsed = FitsDocument.Parse(corrupted);

        Assert.False(parsed.Hdus[0].Integrity.IsValid);
        Assert.True(parsed.Hdus[1].Integrity.IsValid);
        Assert.Throws<FitsException>(() =>
            FitsDocument.Parse(corrupted, new FitsOpenOptions { VerifyChecksum = true }));
    }

    [Fact]
    public void DocumentSpecWrittenSerializerOutputIsValidInAstropyWhenConfigured()
    {
        string root = FindRoot();
        string python;
        try
        {
            python = PythonRuntime.Find(root);
            EnsureAstropy(python);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            throw SkipException.ForSkip($"Astropy oracle is unavailable: {exception.Message}");
        }

        var value = new DirectCompositeFile
        {
            Keywords = new ExposureKeywords { ExposureSeconds = 12.75 },
            Image = new ExposureImage
            {
                Pixels = new float[,] { { 1, 2 }, { 3, 4 } },
            },
            Events = new EventTable { Rows = [new EventRow { Time = 5.5, Channel = 6 }] },
        };
        string path = Path.Combine(Path.GetTempPath(), $"cfits-document-spec-{Guid.NewGuid():N}.fits");
        try
        {
            using (var stream = File.Create(path))
            {
                FitsSerializer.Write(stream, value);
            }

            RunAstropy(python, path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void EnsureAstropy(string python)
    {
        using var process = Process.Start(new ProcessStartInfo(python)
        {
            ArgumentList = { Path.Combine(FindRoot(), "Tools", "CFITS.PyScripts", "Oracles", "CheckAstropy.py") },
            RedirectStandardError = true,
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException("Python did not start.");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("Astropy is not installed.");
        }
    }

    private static void RunAstropy(string python, string path)
    {
        string script = Path.Combine(FindRoot(), "Tools", "CFITS.PyScripts", "Oracles", "ValidateDocument.py");
        using var process = Process.Start(new ProcessStartInfo(python)
        {
            ArgumentList = { script, path },
            RedirectStandardError = true,
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException("Python did not start.");
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CFITS.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("The CFITS repository root was not found.");
    }
}
