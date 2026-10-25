namespace CFITS.Tests;

public sealed class ChecksumTests
{
    [Fact]
    public void EncodingMatchesDivisionBasedReference()
    {
        uint[] values =
        [
            0,
            1,
            0x12345678,
            0x80000000,
            0xFFFFFFFF,
            0xA5A5A5A5,
        ];

        foreach (uint value in values)
        {
            Assert.Equal(ReferenceEncode(value), FitsChecksum.Encode(value));
        }
    }

    [Theory]
    [InlineData(1, 0x01000000u)]
    [InlineData(2, 0x01020000u)]
    [InlineData(3, 0x01020300u)]
    [InlineData(4, 0x01020304u)]
    public void SumHandlesEveryTrailingLength(int length, uint expected)
    {
        byte[] bytes = [1, 2, 3, 4];

        Assert.Equal(expected, FitsChecksum.Sum(bytes.AsSpan(0, length)));
    }

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

        var path = Path.Combine(root, "SampleData", "Compressed", "checksum.fits");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        document.Save(path);
        var python = PythonRuntime.Find(root);
        var script = Path.Combine(root, "Tools", "CFITS.PyScripts", "Fixtures", "CheckChecksum.py");
        var start = new System.Diagnostics.ProcessStartInfo(python)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(script);
        start.ArgumentList.Add(path);
        using var process = System.Diagnostics.Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error + output);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(primary.Header.GetString("DATASUM"), lines[0]);
        Assert.Equal(primary.Header.GetString("CHECKSUM"), lines[1]);

        Assert.Equal(document.ToArray(), document.ToArray());
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

    private static string ReferenceEncode(uint sum)
    {
        int[] excluded = [0x3a, 0x3b, 0x3c, 0x3d, 0x3e, 0x3f, 0x40, 0x5b, 0x5c, 0x5d, 0x5e, 0x5f, 0x60];
        uint value = 0xFFFFFFFF - sum;
        Span<char> assembled = stackalloc char[16];
        Span<int> encoded = stackalloc int[4];
        for (int byteIndex = 0; byteIndex < 4; byteIndex++)
        {
            int piece = (int)((value >> (24 - (8 * byteIndex))) & 0xFF);
            int quotient = (piece / 4) + 0x30;
            encoded.Fill(quotient);
            encoded[0] += piece % 4;
            int pending = 1;
            while (pending != 0)
            {
                pending = 0;
                foreach (int excludedCharacter in excluded)
                {
                    for (int index = 0; index < 4; index += 2)
                    {
                        if (encoded[index] == excludedCharacter || encoded[index + 1] == excludedCharacter)
                        {
                            encoded[index]++;
                            encoded[index + 1]--;
                            pending++;
                        }
                    }
                }
            }

            for (int index = 0; index < 4; index++)
            {
                assembled[(4 * index) + byteIndex] = (char)encoded[index];
            }
        }

        Span<char> characters = stackalloc char[16];
        for (int index = 0; index < characters.Length; index++)
        {
            characters[index] = assembled[(index + 15) % 16];
        }

        return new string(characters);
    }
}
