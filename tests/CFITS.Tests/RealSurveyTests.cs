using System.Diagnostics;

namespace CFITS.Tests;

public class RealSurveyTests
{
    [Fact]
    public void SdssSpectraMatchAstropy()
    {
        var root = FindRoot();
        foreach (var name in new[] { "sdss-spec-0266.fits", "sdss-spec-0285.fits" })
        {
            var path = Path.Combine(root, "Samples", "real", name);
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
        var path = Path.Combine(FindRoot(), ".venv", "Lib", "site-packages", "astropy", "io", "fits", "hdu", "compressed", "tests", "data", "m13_rice.fits");
        if (!File.Exists(path))
        {
            return;
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
        using (var hcomp = FitsDocument.Open(Path.Combine(FindRoot(), ".venv", "Lib", "site-packages", "astropy", "io", "fits", "hdu", "compressed", "tests", "data", "m13_hcomp.fits")))
        {
            Assert.Equal(true, hcomp.Hdus[1].Integrity.UncompressedDataSumMatches);
        }
        Console.WriteLine($"m13 rice pixels {values.Length} sum {sum}");
    }

    private static void AssertImageSum(string name, double expected)
    {
        var path = Path.Combine(FindRoot(), ".venv", "Lib", "site-packages", "astropy", "io", "fits", "hdu", "compressed", "tests", "data", name);
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
