namespace CFITS.Tests;

public class CompressionTests
{
    [Fact]
    public void DecompressesTilesWrittenByAstropy()
    {
        var root = FindRoot();
        var python = Path.Combine(root, ".venv", "Scripts", "python.exe");
        var directory = Path.Combine(root, "Samples", "compressed");
        Directory.CreateDirectory(directory);
        var script = """
            import numpy as np
            from astropy.io import fits
            images = {
                'rice': np.arange(64, dtype=np.int16).reshape(8, 8),
                'gzip': np.arange(48, dtype=np.int32).reshape(6, 8),
                'gzip2': np.array([1.5, np.nan, -2.25, 4], dtype=np.float32).reshape(2, 2),
                'hcomp': np.arange(42, dtype=np.int16).reshape(6, 7) - 20,
            }
            for name, data in images.items():
                kind = {'rice': 'RICE_1', 'gzip': 'GZIP_1', 'gzip2': 'GZIP_2', 'hcomp': 'HCOMPRESS_1'}[name]
                fits.CompImageHDU(data=data, compression_type=kind).writeto(f'{directory}/{name}.fits', overwrite=True)
            smooth = (np.arange(64, dtype=np.int16).reshape(8, 8) * 10)
            comp = fits.CompImageHDU(data=smooth, compression_type='HCOMPRESS_1', hcomp_scale=4, hcomp_smooth=True)
            comp.writeto(f'{directory}/hcomp-smooth.fits', overwrite=True)
            with fits.open(f'{directory}/hcomp-smooth.fits') as hdul:
                np.savetxt(f'{directory}/hcomp-smooth.txt', hdul[1].data.reshape(-1), fmt='%d')
            """;
        var start = new System.Diagnostics.ProcessStartInfo(python, "-c \"" + script.Replace("\"", "\\\"") + "\"")
        {
            WorkingDirectory = root,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        // The embedded script is easier to run from a file.
        var generator = Path.Combine(directory, "compress.py");
        File.WriteAllText(generator, script.Replace("{directory}", directory.Replace("\\", "/")));
        start = new System.Diagnostics.ProcessStartInfo(python, generator)
        {
            WorkingDirectory = root,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        using var process = System.Diagnostics.Process.Start(start)!;
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);

        using var rice = FitsDocument.Open(Path.Combine(directory, "rice.fits"));
        Assert.Equal(FitsHduKind.Image, rice.Hdus[1].Kind);
        var riceValues = rice.Hdus[1].Image!.ReadPhysical();
        for (int index = 0; index < 64; index++)
        {
            Assert.Equal(index, riceValues[index]);
        }

        using var gzip = FitsDocument.Open(Path.Combine(directory, "gzip.fits"));
        var gzipValues = gzip.Hdus[1].Image!.ReadPhysical();
        for (int index = 0; index < 48; index++)
        {
            Assert.Equal(index, gzipValues[index]);
        }

        double[] expected = new double[42];
        for (int index = 0; index < expected.Length; index++)
        {
            expected[index] = index - 20;
        }

        using var hcomp = FitsDocument.Open(Path.Combine(directory, "hcomp.fits"));
        Assert.Equal(expected, hcomp.Hdus[1].Image!.ReadPhysical());

        var smoothExpected = File.ReadAllLines(Path.Combine(directory, "hcomp-smooth.txt")).Select(line => double.Parse(line, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        using var smooth = FitsDocument.Open(Path.Combine(directory, "hcomp-smooth.fits"));
        Assert.Equal(smoothExpected, smooth.Hdus[1].Image!.ReadPhysical());

        using var floats = FitsDocument.Open(Path.Combine(directory, "gzip2.fits"));
        var values = floats.Hdus[1].Image!.ReadPhysical();
        Assert.Equal(1.5, values[0]);
        Assert.True(double.IsNaN(values[1]));
        Assert.Equal(-2.25, values[2]);
        Assert.Equal(4, values[3]);
    }

    [Fact]
    public void WritesGzipTilesTheRuntimeCanCompress()
    {
        short[] pixels = [1, -2, 3, 4, 5, 6];
        using var document = FitsDocumentBuilder.Create()
            .GzipImage([3, 2], pixels)
            .Build();
        var image = document.Hdus[1].Image!;
        Assert.Equal(pixels.Select(value => (double)value).ToArray(), image.ReadPhysical());

        float[] samples = [1.5f, float.NaN, -2.25f, 4f];
        using var shuffled = FitsDocumentBuilder.Create()
            .GzipImage([2, 2], samples, shuffle: true)
            .Build();
        var values = shuffled.Hdus[1].Image!.ReadPhysical();
        Assert.Equal(1.5, values[0]);
        Assert.True(double.IsNaN(values[1]));
        Assert.Equal(-2.25, values[2]);
        Assert.Equal(4, values[3]);
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
