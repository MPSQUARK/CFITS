using System.Diagnostics;

namespace CFITS.Tests;

public sealed class OfficialTiledImageCompressionTests
{
    [Fact]
    public void ReadsOfficialGzipRiceHcompressAndPlioTilesIncludingPartialEdges()
    {
        string root = FindRoot();
        string directory = Path.Combine(Path.GetTempPath(), "cfits-compliance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string script = Path.Combine(root, "Tools", "CFITS.PyScripts", "Fixtures", "GenerateOfficialTiledImages.py");
            RunPython(root, script, directory);
            AssertImage(Path.Combine(directory, "gzip1.fits"), Enumerable.Range(0, 35).Select(value => (double)value).ToArray());
            AssertImage(Path.Combine(directory, "gzip2.fits"), Enumerable.Range(0, 35).Select(value => (double)value).ToArray());
            AssertImage(Path.Combine(directory, "rice.fits"), Enumerable.Range(0, 35).Select(value => (double)value).ToArray());
            AssertImage(Path.Combine(directory, "hcompress.fits"), Enumerable.Range(0, 64).Select(value => (double)(value - 32)).ToArray(), [8, 8]);
            AssertImage(Path.Combine(directory, "plio.fits"), Enumerable.Range(0, 35).Select(value => (double)value).ToArray());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void AssertImage(string path, double[] expected, int[]? axes = null)
    {
        using var document = FitsDocument.Open(path);
        FitsImage image = document.Hdus[1].Image!;
        Assert.Equal(axes ?? [7, 5], image.Axes);
        Assert.Equal(expected, image.ReadPhysical());
    }

    private static void RunPython(string root, string script, string directory)
    {
        string python = PythonRuntime.Find(root);
        var start = new ProcessStartInfo(python)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(script);
        start.ArgumentList.Add(directory);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Python.");
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"Astropy is required for this interoperability test. Install astropy in the configured Python: {error}");
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
