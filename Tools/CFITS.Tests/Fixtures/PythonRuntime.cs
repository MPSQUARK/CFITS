using System.Diagnostics;

namespace CFITS.Tests;

internal static class PythonRuntime
{
    public static string Find(string root)
    {
        string? configured = Environment.GetEnvironmentVariable("CFITS_PYTHON");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        string[] candidates =
        [
            Path.Combine(root, ".venv", "Scripts", "python.exe"),
            Path.Combine(root, ".venv", "bin", "python"),
            "python3",
            "python",
            "py",
        ];
        string? executable = candidates.FirstOrDefault(candidate => Path.IsPathRooted(candidate) && File.Exists(candidate));
        if (executable is not null)
        {
            return executable;
        }

        foreach (string candidate in candidates.Where(candidate => !Path.IsPathRooted(candidate)))
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo(candidate)
                {
                    ArgumentList = { "--version" },
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                });
                if (process is not null)
                {
                    process.WaitForExit();
                    if (process.ExitCode == 0)
                    {
                        return candidate;
                    }
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
            catch (FileNotFoundException)
            {
            }
        }

        throw new InvalidOperationException("Python was not found. Set CFITS_PYTHON or install Python 3.");
    }

    public static string FindAstropyData(string root, string fileName)
    {
        string python = Find(root);
        var start = new ProcessStartInfo(python)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("import importlib.resources as r; print(r.files('astropy').joinpath('io', 'fits', 'hdu', 'compressed', 'tests', 'data', __import__('sys').argv[1]))");
        start.ArgumentList.Add(fileName);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {python}.");
        string output = process.StandardOutput.ReadToEnd().Trim();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0 || !File.Exists(output))
        {
            throw new InvalidOperationException($"Astropy test data '{fileName}' was not found: {error}");
        }

        return output;
    }
}
