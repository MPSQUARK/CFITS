using System.Diagnostics;
using System.Security.Cryptography;

namespace CFITS.Tests;

public sealed class RealSurveyTests
{
    [Fact]
    public void FiveSdssFilesReadAndWriteAsWholeFitsFiles()
    {
        string root = FindRoot();
        string[] names =
        [
            "sdss-spec-0266.fits",
            "sdss-spec-0285.fits",
            "sdss-spec-0500-001.fits",
            "sdss-spec-0500-002.fits",
            "sdss-spec-0500-003.fits",
        ];
        string manifestPath = Path.Combine(root, "Benchmarks", "Results", "sdss-manifest.csv");
        var expectedHashes = File.ReadLines(manifestPath)
            .Skip(1)
            .Select(line => line.Split(','))
            .ToDictionary(fields => fields[0], fields => fields[2], StringComparer.Ordinal);
        string outputDirectory = Path.Combine(Path.GetTempPath(), "cfits-sdss-roundtrip", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);
        try
        {
            foreach (string name in names)
            {
                string source = Path.Combine(root, "SampleData", "Real", name);
                string output = Path.Combine(outputDirectory, name);
                Assert.True(File.Exists(source), source);
                Assert.Equal(
                    expectedHashes[name],
                    Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))));

                using var document = FitsDocument.Open(source);
                int hduCount = document.Hdus.Count;
                document.Save(output);
                Assert.True(new FileInfo(output).Length > 0, output);

                using var roundTrip = FitsDocument.Open(output);
                Assert.Equal(hduCount, roundTrip.Hdus.Count);
                Assert.Equal(
                    document.Hdus.Select(GetHduType),
                    roundTrip.Hdus.Select(GetHduType));
                Assert.Equal(
                    document.Hdus.Select(hdu => hdu.RawData.Length),
                    roundTrip.Hdus.Select(hdu => hdu.RawData.Length));
                for (int index = 0; index < hduCount; index++)
                {
                    if (document.Hdus[index].Table is { } table)
                    {
                        Assert.Equal(table.RowCount, roundTrip.Hdus[index].Table?.RowCount);
                    }
                }
            }
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    private static string? GetHduType(FitsHdu hdu)
    {
        if (hdu.Header.TryGetCard("SIMPLE", out var simple))
        {
            return "PRIMARY";
        }

        return hdu.Header.TryGetCard("XTENSION", out var extension) ? extension.GetString() : null;
    }

    [Fact]
    public void SdssSpectraMatchAstropy()
    {
        var root = FindRoot();
        foreach (var name in new[] { "sdss-spec-0266.fits", "sdss-spec-0285.fits" })
        {
            var path = Path.Combine(root, "SampleData", "Real", name);
            Assert.True(File.Exists(path), path);
            using var document = FitsDocument.Open(path);
            var coadd = document.Hdus[1];
            Assert.Equal("COADD", coadd.Header.GetString("EXTNAME"));
            var flux = coadd.Table!.Column("flux");
            var wavelength = coadd.Table.Column("loglam");
            double firstFlux = flux.ReadReal(0);
            double firstWavelength = wavelength.ReadReal(0);
            if (name == "sdss-spec-0266.fits")
            {
                Assert.Equal(3835, coadd.Table.RowCount);
                Assert.True(Math.Abs(firstFlux - 54.982757568359375) < 1e-4, firstFlux.ToString());
                Assert.True(Math.Abs(firstWavelength - 3.5804998874664307) < 1e-6, firstWavelength.ToString());
            }
            double sum = 0;
            var watch = Stopwatch.StartNew();
            for (int repeat = 0; repeat < 10; repeat++)
            {
                using var again = FitsDocument.Open(path);
                var table = again.Hdus[1].Table!;
                var column = table.Column("flux");
                sum = 0;
                for (int row = 0; row < table.RowCount; row++)
                {
                    sum += column.ReadReal(row);
                }
            }

            watch.Stop();
            Assert.True(sum != 0);
            Assert.InRange(watch.Elapsed.TotalMilliseconds / 10, 0, 500);
            Console.WriteLine($"{name}: first flux {firstFlux}, loglam {firstWavelength}, sum {sum}, {watch.Elapsed.TotalMilliseconds / 10:0.0} ms");
        }
    }

    [Fact]
    public void Messier13RiceImageMatchesAstropy()
    {
        string root = FindRoot();
        string path;
        try
        {
            path = PythonRuntime.FindAstropyData(root, "m13_rice.fits");
        }
        catch (InvalidOperationException exception)
        {
            throw Xunit.Sdk.SkipException.ForSkip($"Astropy compressed-image fixtures are unavailable: {exception.Message}");
        }

        using var document = FitsDocument.Open(path);
        var image = document.Hdus[1].Image!;
        double sum = 0;
        var values = image.ReadPhysical();
        foreach (double value in values)
        {
            if (!double.IsNaN(value))
            {
                sum += value;
            }
        }

        Assert.Equal(90000, values.Length);
        Assert.Equal(13293397, sum);
        Assert.Equal(112, values[0]);
        Assert.Equal(113, values[2]);
        AssertImageSum("m13_gzip.fits", 13293397);
        AssertImageSum("m13_plio.fits", 13293397);
        AssertImageSum("m13_hcomp.fits", 13293397);
        using (var hcomp = FitsDocument.Open(PythonRuntime.FindAstropyData(root, "m13_hcomp.fits")))
        {
            Assert.Equal(true, hcomp.Hdus[1].Integrity.UncompressedDataSumMatches);
        }
        Console.WriteLine($"m13 rice pixels {values.Length} sum {sum}");
    }

    private static void AssertImageSum(string name, double expected)
    {
        var path = PythonRuntime.FindAstropyData(FindRoot(), name);
        using var document = FitsDocument.Open(path);
        double sum = 0;
        foreach (double value in document.Hdus[1].Image!.ReadPhysical())
        {
            if (!double.IsNaN(value))
            {
                sum += value;
            }
        }

        Assert.Equal(expected, sum);
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
