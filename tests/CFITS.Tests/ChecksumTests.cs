namespace CFITS.Tests;

public class ChecksumTests
{
    [Fact]
    public void StampedImageMatchesItsKeywordsAndAstropy()
    {
        var root = FindRoot();
        short[] pixels = [1, 2, 3, 4, 5, 6];
        using var document = FitsDocumentBuilder.Create()
            .Image([6], pixels)
            .Checksum()
            .Build();
        var primary = document.Hdus[0];
        Assert.Equal(true, primary.Integrity.ChecksumMatches);
        Assert.Equal(true, primary.Integrity.DataSumMatches);
        Assert.True(primary.Integrity.DataSum != 0);

        var path = Path.Combine(root, "Samples", "compressed", "checksum.fits");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        document.Save(path);
        var python = Path.Combine(root, ".venv", "Scripts", "python.exe");
        var script = Path.Combine(root, "Samples", "compressed", "checksum.py");
        string scriptText = "from astropy.io import fits\n" +
            "with fits.open(r'" + path.Replace("\\", "/") + "', checksum=True) as hdul:\n" +
            "    hdul.verify('exception')\n" +
            "    print(hdul[0].header['DATASUM'])\n" +
            "    print(hdul[0].header['CHECKSUM'])\n";
        File.WriteAllText(script, scriptText);
        var start = new System.Diagnostics.ProcessStartInfo(python, script)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        using var process = System.Diagnostics.Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error + output);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(primary.Header.GetString("DATASUM"), lines[0]);
        Assert.Equal(primary.Header.GetString("CHECKSUM"), lines[1]);

        var broken = document.ToArray();
        broken[^1] ^= 0x01;
        using var corrupted = FitsDocument.Parse(broken);
        Assert.False(corrupted.Hdus[0].Integrity.IsValid);
        Assert.Throws<FitsException>(() => FitsDocument.Parse(broken, new FitsOpenOptions { VerifyChecksum = true }));
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
