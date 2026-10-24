using System.Diagnostics;
using System.Text;

namespace CFITS.Tests;

public sealed class OracleTests
{
    private static readonly string Root = FindRoot();
    private static readonly string Python = PythonRuntime.Find(Root);
    private static readonly string Oracle = Path.Combine(Root, "Tools", "SampleGenerator", "Source", "oracle.py");
    private static readonly string Fixtures = Path.Combine(Root, "SampleData", "Generated");

    [Fact]
    public void AstropyFixturesMatchTheReaderAndTheWriter()
    {
        Run("generate", Fixtures);
        foreach (var fits in Directory.GetFiles(Fixtures, "*.fits"))
        {
            using var document = FitsDocument.Open(fits);
            if (Path.GetFileName(fits) == "special.fits")
            {
                Assert.Equal(2880, document.SpecialRecords.Length);
                Assert.Equal("SPECIAL!"u8.ToArray(), document.SpecialRecords.Span[..8].ToArray());
                Assert.Equal(new double[] { 0, 1, 2, 3 }, document.Hdus[0].Image!.ReadPhysical());
                var specialCopy = Path.Combine(Path.GetTempPath(), "cfits-special.fits");
                document.Save(specialCopy);
                using var roundTrip = FitsDocument.Open(specialCopy);
                Assert.Equal("SPECIAL!"u8.ToArray(), roundTrip.SpecialRecords.Span[..8].ToArray());
                Assert.Equal(new double[] { 0, 1, 2, 3 }, roundTrip.Hdus[0].Image!.ReadPhysical());
                continue;
            }
            var dump = Path.Combine(Path.GetTempPath(), Path.GetFileName(fits) + ".json");
            File.WriteAllText(dump, OracleDump.Create(document));
            Run("check-dump", fits, dump);

            var copy = Path.Combine(Path.GetTempPath(), "cfits-" + Path.GetFileName(fits));
            document.Save(copy);
            Run("compare-files", fits, copy);
        }
    }

    private static void Run(params string[] arguments)
    {
        var start = new ProcessStartInfo(Python)
        {
            WorkingDirectory = Root,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(Oracle);
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Python did not start.");
        var output = new StringBuilder();
        process.OutputDataReceived += (_, eventArgs) => output.AppendLine(eventArgs.Data);
        process.ErrorDataReceived += (_, eventArgs) => output.AppendLine(eventArgs.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, output.ToString());
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CFITS.slnx")) || File.Exists(Path.Combine(directory.FullName, "CFITS.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("The CFITS repository root was not found.");
    }
}
