using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace CFITS.Tests;

public sealed class DifferentialFixtureTests
{
    [Fact]
    public void ImportsEveryCheckedInDifferentialFixtureWithStableProvenance()
    {
        string root = FindRoot();
        var manifest = JsonSerializer.Deserialize<FixtureManifest>(
            File.ReadAllText(Path.Combine(root, "Tools", "CFITS.Tests", "Fixtures", "DifferentialFixtures.json")))
            ?? throw new InvalidOperationException("The differential fixture manifest is empty.");

        Assert.Equal("Astropy 8.0.2; CFITSIO-compatible FITS wire format", manifest.Provenance);
        Assert.NotEmpty(manifest.Fixtures);
        foreach (Fixture fixture in manifest.Fixtures)
        {
            string path = Path.Combine(root, fixture.Path.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"Missing checked-in fixture: {fixture.Path}");
            Assert.Equal(fixture.Sha256, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
            using FitsDocument document = FitsDocument.Open(path);
            Assert.NotEmpty(document.Hdus);
        }
    }

    [Fact]
    public void VerifiesAstropyRandomGroupsFixtureOffline()
    {
        using FitsDocument document = FitsDocument.Open(FixturePath("SampleData/Generated/groups.fits"));
        FitsGroups groups = document.Hdus[0].Groups!;

        Assert.Equal(2, groups.Count);
        Assert.Equal(2, groups.ParameterCount);
        Assert.Equal(3, groups.DataLength);
        Assert.Equal([1, 3], [groups.ReadParameter(0, 0), groups.ReadParameter(0, 1)]);
        Assert.Equal([2, 4], [groups.ReadParameter(1, 0), groups.ReadParameter(1, 1)]);
        Assert.Equal([0, 1, 2], [groups.ReadData(0, 0), groups.ReadData(0, 1), groups.ReadData(0, 2)]);
        Assert.Equal([3, 4, 5], [groups.ReadData(1, 0), groups.ReadData(1, 1), groups.ReadData(1, 2)]);
    }

    [Fact]
    public void VerifiesAstropyWcsFixturesOffline()
    {
        foreach (string name in new[] { "linear", "tan", "sin", "arc", "zea", "stg", "car", "pc" })
        {
            using FitsDocument document = FitsDocument.Open(FixturePath($"SampleData/Compressed/{name}.fits"));
            FitsWorld world = FitsWorld.FromHeader(document.Hdus[0].Header);
            double[] pixels = ReadValues(FixturePath($"SampleData/Compressed/{name}-pix.txt"));
            double[] expected = ReadValues(FixturePath($"SampleData/Compressed/{name}.txt"));
            Assert.Equal(expected.Length, pixels.Length);
            Assert.Equal(expected.Length, world.Axes * 4);
            for (int offset = 0; offset < expected.Length; offset += world.Axes)
            {
                double[] actual = world.PixelToWorld(pixels.AsSpan(offset, world.Axes));
                for (int axis = 0; axis < world.Axes; axis++)
                {
                    Assert.Equal(expected[offset + axis], actual[axis], 8);
                }
            }

            Assert.Equal(name == "linear" ? "WAVE" : "RA---" + NameProjection(name), world.AxisMetadata[0].Type[..Math.Min(world.AxisMetadata[0].Type.Length, 8)]);
        }
    }

    [Fact]
    public void VerifiesCheckedInCompressedImageAndTableFixturesOffline()
    {
        AssertImage("gzip.fits", Enumerable.Range(0, 48).Select(static value => (double)value).ToArray());
        AssertImage("gzip2.fits", [1.5, double.NaN, -2.25, 4], [2, 2]);
        AssertImage("rice.fits", Enumerable.Range(0, 64).Select(static value => (double)value).ToArray(), [8, 8]);
        AssertImage("hcomp.fits", Enumerable.Range(0, 42).Select(static value => (double)(value - 20)).ToArray(), [7, 6]);
        AssertImage("tiled.fits", Enumerable.Range(0, 35).Select(static value => (double)value).ToArray(), [7, 5]);

        using FitsDocument document = FitsDocument.Open(FixturePath("SampleData/Compressed/ztable.fits"));
        FitsTable table = document.Hdus[1].Table!;
        Assert.Equal([1.5, -2.25, 4, 8.5], Enumerable.Range(0, 4).Select(index => table.Column("flux").ReadReal(index)).ToArray());
        Assert.Equal([10L, -3L, 8L, 4L], Enumerable.Range(0, 4).Select(index => table.Column("id").ReadInteger(index)).ToArray());
        FitsColumn samples = document.Hdus[3].Table!.Column("samples");
        Assert.Equal(2, samples.Count(0));
        Assert.Equal([1L, 2L], samples.ReadIntegerVector(0));
        Assert.Equal([3L], samples.ReadIntegerVector(1));
    }

    private static void AssertImage(string name, double[] expected, int[]? axes = null)
    {
        using FitsDocument document = FitsDocument.Open(FixturePath($"SampleData/Compressed/{name}"));
        FitsImage image = document.Hdus[1].Image!;
        if (axes is not null)
        {
            Assert.Equal(axes, image.Axes);
        }

        double[] actual = image.ReadPhysical();
        Assert.Equal(expected.Length, actual.Length);
        for (int index = 0; index < actual.Length; index++)
        {
            if (double.IsNaN(expected[index]))
            {
                Assert.True(double.IsNaN(actual[index]));
            }
            else
            {
                Assert.Equal(expected[index], actual[index], 8);
            }
        }
    }

    private static double[] ReadValues(string path) =>
        File.ReadAllText(path)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => double.Parse(value, CultureInfo.InvariantCulture))
            .ToArray();

    private static string NameProjection(string name) =>
        name.ToUpperInvariant() switch
        {
            "PC" => "TAN",
            _ => name.ToUpperInvariant(),
        };

    private static string FixturePath(string relativePath) =>
        Path.Combine(FindRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

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

    private sealed record FixtureManifest(string Provenance, Fixture[] Fixtures);

    private sealed record Fixture(string Path, string Sha256);
}
