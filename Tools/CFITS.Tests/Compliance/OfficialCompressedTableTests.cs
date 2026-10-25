using System.Diagnostics;

namespace CFITS.Tests;

public sealed class OfficialCompressedTableTests
{
    [Fact]
    public void ReadsGzipOneGzipTwoRiceAndBothVariableDescriptorWidths()
    {
        string root = FindRoot();
        string directory = Path.Combine(Path.GetTempPath(), "cfits-table-compliance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string script = Path.Combine(root, "Tools", "CFITS.PyScripts", "Fixtures", "GenerateOfficialCompressedTables.py");
            RunPython(root, script, directory);
            AssertValues(Path.Combine(directory, "gzip1.fits"));
            AssertValues(Path.Combine(directory, "gzip2.fits"));
            AssertPlainGzipTwoIsNotSilentlyAccepted(Path.Combine(directory, "gzip2-plain.fits"));
            AssertValues(Path.Combine(directory, "rice.fits"));
            AssertVariableValues(Path.Combine(directory, "p.fits"));
            AssertVariableValues(Path.Combine(directory, "q.fits"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void AssertValues(string path)
    {
        using var document = FitsDocument.Open(path);
        var column = document.Hdus[1].Table!.Column("VALUES");
        Assert.Equal(new long[] { 10, -3, 8, 4 }, Enumerable.Range(0, 4).Select(index => column.ReadInteger(index)).ToArray());
    }

    private static void AssertPlainGzipTwoIsNotSilentlyAccepted(string path)
    {
        using var document = FitsDocument.Open(path);
        var column = document.Hdus[1].Table!.Column("VALUES");
        Assert.NotEqual(new long[] { 10, -3, 8, 4 }, Enumerable.Range(0, 4).Select(index => column.ReadInteger(index)).ToArray());
    }

    private static void AssertVariableValues(string path)
    {
        using var document = FitsDocument.Open(path);
        var column = document.Hdus[1].Table!.Column("VALUES");
        Assert.Equal(2, column.Count(0));
        Assert.Equal(10, column.ReadInteger(0, 0));
        Assert.Equal(20, column.ReadInteger(0, 1));
        Assert.Equal(1, column.Count(1));
        Assert.Equal(30, column.ReadInteger(1, 0));
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
