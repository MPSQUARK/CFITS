using System.Diagnostics;
using System.Globalization;
using Xunit.Sdk;

namespace CFITS.Tests;

public sealed class MissingProjectionConventionTests
{
    public static IEnumerable<object[]> Projections() =>
    [
        ["AZP", 0d, 0d],
        ["CEA", 1d, double.NaN],
        ["AIT", double.NaN, double.NaN],
        ["HPX", double.NaN, double.NaN],
    ];

    [Theory]
    [MemberData(nameof(Projections))]
    public void RepresentativeProjectionMatchesAstropy(string projection, double pv1, double pv2)
    {
        string root = FindRoot();
        string python;
        try
        {
            python = PythonRuntime.Find(root);
        }
        catch (Exception exception)
        {
            throw SkipException.ForSkip($"Astropy oracle is unavailable: {exception.Message}");
        }

        string script = Path.Combine(root, "Tools", "CFITS.PyScripts", "Oracles", "EvaluateProjection.py");
        double[] expected = RunAstropy(python, script, projection);

        var builder = FitsHeader.Builder()
            .Integer("WCSAXES", 2)
            .Real("CRPIX1", 1)
            .Real("CRPIX2", 1)
            .Real("CRVAL1", 30)
            .Real("CRVAL2", 40)
            .Real("CDELT1", -0.1)
            .Real("CDELT2", 0.1)
            .Text("CTYPE1", "RA---" + projection)
            .Text("CTYPE2", "DEC--" + projection);
        if (double.IsFinite(pv1))
        {
            builder = builder.Real("PV2_1", pv1);
        }

        if (double.IsFinite(pv2))
        {
            builder = builder.Real("PV2_2", pv2);
        }

        var world = FitsWorld.FromHeader(builder.Build());
        double[] actual = world.PixelToWorld([2, 3]);

        Assert.Equal(expected[0], actual[0], 10);
        Assert.Equal(expected[1], actual[1], 10);
    }

    [Fact]
    public void RejectsUnsupportedZpnPolynomial()
    {
        var error = Assert.Throws<FitsException>(() => FitsWorld.FromHeader(FitsHeader.Builder()
            .Integer("WCSAXES", 2)
            .Real("CRPIX1", 1)
            .Real("CRPIX2", 1)
            .Real("CRVAL1", 30)
            .Real("CRVAL2", 40)
            .Real("CDELT1", -0.1)
            .Real("CDELT2", 0.1)
            .Text("CTYPE1", "RA---ZPN")
            .Text("CTYPE2", "DEC--ZPN")
            .Real("PV2_1", 0)
            .Real("PV2_2", 2)
            .Build()));

        Assert.Contains("external", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsUnsupportedAzpParameters()
    {
        var error = Assert.Throws<FitsException>(() => FitsWorld.FromHeader(FitsHeader.Builder()
            .Integer("WCSAXES", 2)
            .Real("CRPIX1", 1)
            .Real("CRPIX2", 1)
            .Real("CRVAL1", 30)
            .Real("CRVAL2", 40)
            .Real("CDELT1", -0.1)
            .Real("CDELT2", 0.1)
            .Text("CTYPE1", "RA---AZP")
            .Text("CTYPE2", "DEC--AZP")
            .Real("PV2_1", 45)
            .Real("PV2_2", 0.5)
            .Build()));

        Assert.Contains("external", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static double[] RunAstropy(string python, string script, string projection)
    {
        using var process = Process.Start(new ProcessStartInfo(python)
        {
            ArgumentList = { script, projection },
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException($"Could not start {python}.");
        string output = process.StandardOutput.ReadToEnd().Trim();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw SkipException.ForSkip($"Astropy oracle could not evaluate {projection}: {error}");
        }

        return output.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => double.Parse(value, CultureInfo.InvariantCulture))
            .ToArray();
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
