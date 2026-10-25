namespace CFITS.Tests;

public sealed class CoordinateTests
{
    [Fact]
    public void PixelToWorldSupportsAliasedSpans()
    {
        var world = FitsWorld.FromHeader(FitsHeader.Builder()
            .Integer("WCSAXES", 2)
            .Real("CRPIX1", 0)
            .Real("CRPIX2", 0)
            .Real("CRVAL1", 10)
            .Real("CRVAL2", 20)
            .Real("CDELT1", 1)
            .Real("CDELT2", 1)
            .Build());
        double[] coordinates = [1, 2];

        world.PixelToWorld(coordinates, coordinates);

        Assert.Equal([11, 22], coordinates);
    }

    [Fact]
    public void WorldToPixelRejectsNearSingularMatrix()
    {
        var world = FitsWorld.FromHeader(FitsHeader.Builder()
            .Integer("WCSAXES", 2)
            .Real("CD1_1", 1)
            .Real("CD1_2", 1)
            .Real("CD2_1", 1)
            .Real("CD2_2", 1 + 1e-14)
            .Build());

        Assert.Throws<FitsException>(() => world.WorldToPixel([1, 1]));
    }

    [Fact]
    public void WorldToPixelWritesIntoDestinationSpan()
    {
        var world = FitsWorld.FromHeader(FitsHeader.Builder()
            .Integer("WCSAXES", 2)
            .Real("CRPIX1", 2)
            .Real("CRPIX2", 3)
            .Real("CRVAL1", 10)
            .Real("CRVAL2", 20)
            .Real("CDELT1", 2)
            .Real("CDELT2", 4)
            .Build());
        Span<double> pixel = stackalloc double[3];

        world.WorldToPixel([14, 28], pixel);

        Assert.Equal(3, pixel.Length);
        Assert.Equal(4, pixel[0]);
        Assert.Equal(5, pixel[1]);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1000)]
    public void RejectsInvalidWcsAxes(long axes)
    {
        var header = FitsHeader.Builder().Integer("WCSAXES", axes).Build();

        var error = Assert.Throws<FitsException>(() => FitsWorld.FromHeader(header));

        Assert.Contains("WCSAXES", error.Message);
    }

    [Fact]
    public void WorldCoordinatesMatchAstropy()
    {
        var root = FindRoot();
        var directory = Path.Combine(root, "SampleData", "Compressed");
        Directory.CreateDirectory(directory);
        var python = PythonRuntime.Find(root);
        var start = new System.Diagnostics.ProcessStartInfo(python)
        {
            WorkingDirectory = directory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(Path.Combine(root, "Tools", "CFITS.PyScripts", "Fixtures", "GenerateWcs.py"));
        start.ArgumentList.Add(directory);
        using var process = System.Diagnostics.Process.Start(start)!;
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);

        double[][] pixels = [[2, 3], [3, 3], [2, 4], [4, 6]];
        foreach (var name in new[] { "linear", "tan", "sin", "arc", "zea", "stg", "car", "pc" })
        {
            using var document = FitsDocument.Open(Path.Combine(directory, name + ".fits"));
            var world = FitsWorld.FromHeader(document.Hdus[0].Header);
            var expected = File.ReadAllLines(Path.Combine(directory, name + ".txt"));
            var expectedPixels = File.ReadAllLines(Path.Combine(directory, name + "-pix.txt"));
            for (int index = 0; index < pixels.Length; index++)
            {
                var actual = world.PixelToWorld(pixels[index]);
                var parts = expected[index].Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);
                double expectedLongitude = double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
                double expectedLatitude = double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                Assert.True(Math.Abs(expectedLongitude - actual[0]) < 1e-8 && Math.Abs(expectedLatitude - actual[1]) < 1e-8, $"{name}[{index}] {actual[0]} {actual[1]} expected {expected[index]}");
                var back = world.WorldToPixel([expectedLongitude, expectedLatitude]);
                var pixelParts = expectedPixels[index].Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);
                Assert.True(Math.Abs(double.Parse(pixelParts[0], System.Globalization.CultureInfo.InvariantCulture) - back[0]) < 1e-6, $"{name} pixel {back[0]} {back[1]} expected {expectedPixels[index]}");
                Assert.True(Math.Abs(double.Parse(pixelParts[1], System.Globalization.CultureInfo.InvariantCulture) - back[1]) < 1e-6, $"{name} pixel {back[0]} {back[1]} expected {expectedPixels[index]}");
            }
        }
    }

    [Fact]
    public void ClockConvertsReferenceAndUnits()
    {
        Assert.Equal(51544, FitsClock.ModifiedJulianDate("2000-01-01"), 9);
        Assert.Equal(51544.5, FitsClock.ModifiedJulianDate("2000-01-01T12:00:00"), 9);
        Assert.Equal(51544.5 - (2d / 24d), FitsClock.ModifiedJulianDate("2000-01-01T12:00:00+02:00"), 9);
        Assert.Equal(50000 + (32.184d / 86400d), FitsClock.ConvertModifiedJulianDate(50000, "TAI", "TT"), 12);
        Assert.Equal(50000, FitsClock.ConvertModifiedJulianDate(FitsClock.ConvertModifiedJulianDate(50000, "TT", "TCG"), "TCG", "TT"), 9);
        Assert.Equal(50000 + (29d / 86400d), FitsClock.ConvertModifiedJulianDate(50000, "UTC", "TAI"), 12);
        var header = FitsHeader.Builder()
            .Text("TIMESYS", "UTC")
            .Integer("MJDREFI", 50000)
            .Real("MJDREFF", 0.25)
            .Real("TIMEOFFS", 0.5)
            .Text("TIMEUNIT", "s")
            .Build();
        var clock = FitsClock.FromHeader(header);
        Assert.Equal("UTC", clock.Scale);
        Assert.Equal(50000.25 + (10.5d / 86400d), clock.ToModifiedJulianDate(10), 9);
        Assert.Equal(365.24219040211236, FitsClock.TropicalYearInDays(0), 12);
        Assert.Equal(365.2421987817, FitsClock.BesselianYearInDays(0), 12);
        var instant = clock.ToDateTimeOffset(10);
        Assert.Equal(DateTimeKind.Utc, instant.UtcDateTime.Kind);
        Assert.Equal(50000.25 + (10.5d / 86400d), FitsClock.ModifiedJulianDate(instant), 9);
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
