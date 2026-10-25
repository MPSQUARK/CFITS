using System.Diagnostics;
using System.Globalization;
using Xunit.Sdk;

namespace CFITS.Tests;

public sealed class WcsPvConventionTests
{
    [Fact]
    public void TanProjectionAppliesPvParameters()
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
        double[] expected = RunAstropy(python, script);

        var world = FitsWorld.FromHeader(FitsHeader.Builder()
            .Integer("WCSAXES", 2)
            .Real("CRPIX1", 1)
            .Real("CRPIX2", 1)
            .Real("CRVAL1", 30)
            .Real("CRVAL2", 40)
            .Real("CDELT1", -0.1)
            .Real("CDELT2", 0.1)
            .Text("CTYPE1", "RA---TAN")
            .Text("CTYPE2", "DEC--TAN")
            .Real("PV2_1", 45)
            .Build());

        double[] actual = world.PixelToWorld([2, 3]);

        Assert.Equal(expected[0], actual[0], 10);
        Assert.Equal(expected[1], actual[1], 10);
    }

    private static double[] RunAstropy(string python, string script)
    {
        using var process = Process.Start(new ProcessStartInfo(python)
        {
            ArgumentList = { script, "TAN" },
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException($"Could not start {python}.");
        string output = process.StandardOutput.ReadToEnd().Trim();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw SkipException.ForSkip($"Astropy oracle could not evaluate PV: {error}");
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
