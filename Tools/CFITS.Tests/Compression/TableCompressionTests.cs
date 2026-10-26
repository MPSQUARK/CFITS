using System.Buffers.Binary;

namespace CFITS.Tests;

public sealed class TableCompressionTests
{
    [Fact]
    public void NocompressVariableExpansionPreservesHeapBytes()
    {
        byte[] expected = [0, 0, 0, 1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0, 4];
        using var document = CreateNocompressTable(expected, compressedCount: expected.Length, compressedOffset: 32, heapOffset: 0);

        FitsTable table = document.Hdus[1].Table!;
        Assert.Equal(expected, table.Column("COMPRESSED").ReadVariableBytes(0));
    }

    [Theory]
    [InlineData(17, 32, 0)]
    [InlineData(16, 40, 0)]
    [InlineData(16, 32, 1)]
    public void RejectsMalformedNocompressVariableDescriptor(long compressedCount, long compressedOffset, long heapOffset)
    {
        Assert.Throws<FitsException>(() =>
        {
            using var document = CreateNocompressTable(
                [0, 0, 0, 1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0, 4],
                compressedCount,
                compressedOffset,
                heapOffset);
            _ = document.Hdus[1].Table;
        });
    }

    [Fact]
    public void DecompressesFixedAndVariableColumns()
    {
        var root = FindRoot();
        var directory = Path.Combine(root, "SampleData", "Compressed");
        Directory.CreateDirectory(directory);
        var python = PythonRuntime.Find(root);
        var generator = Path.Combine(root, "Tools", "CFITS.PyScripts", "Fixtures", "GenerateTableCompression.py");
        var start = new System.Diagnostics.ProcessStartInfo(python)
        {
            WorkingDirectory = directory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(generator);
        start.ArgumentList.Add(directory);
        using var process = System.Diagnostics.Process.Start(start)!;
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);

        using var document = FitsDocument.Open(Path.Combine(directory, "ztable.fits"));
        var table = document.Hdus[1].Table!;
        var flux = table.Column("flux");
        Assert.Equal(1.5, flux.ReadReal(0), 6);
        Assert.Equal(-2.25, flux.ReadReal(1), 6);
        Assert.Equal(4, flux.ReadReal(2), 6);
        Assert.Equal(8.5, flux.ReadReal(3), 6);
        var ident = table.Column("id");
        Assert.Equal(10, ident.ReadInteger(0));
        Assert.Equal(-3, ident.ReadInteger(1));
        Assert.Equal(8, ident.ReadInteger(2));
        Assert.Equal(4, ident.ReadInteger(3));

        var rice = document.Hdus[2].Table!.Column("value");
        Assert.Equal(10, rice.ReadInteger(0));
        Assert.Equal(-3, rice.ReadInteger(1));
        Assert.Equal(8, rice.ReadInteger(2));
        Assert.Equal(4, rice.ReadInteger(3));

        var samples = document.Hdus[3].Table!.Column("samples");
        Assert.Equal(2, samples.Count(0));
        Assert.Equal(1, samples.ReadInteger(0, 0));
        Assert.Equal(2, samples.ReadInteger(0, 1));
        Assert.Equal(1, samples.Count(1));
        Assert.Equal(3, samples.ReadInteger(1, 0));
    }

    private static FitsDocument CreateNocompressTable(byte[] payload, long compressedCount, long compressedOffset, long heapOffset)
    {
        const int bundleLength = 32;
        byte[] bundle = new byte[bundleLength + payload.Length];
        BinaryPrimitives.WriteInt32BigEndian(bundle.AsSpan(0, 4), checked((int)compressedCount));
        BinaryPrimitives.WriteInt32BigEndian(bundle.AsSpan(4, 4), checked((int)compressedOffset));
        BinaryPrimitives.WriteInt32BigEndian(bundle.AsSpan(8, 4), 4);
        BinaryPrimitives.WriteInt32BigEndian(bundle.AsSpan(12, 4), checked((int)heapOffset));
        payload.CopyTo(bundle.AsSpan(bundleLength));
        string bytes = new(bundle.Select(static value => (char)value).ToArray());

        return FitsDocumentBuilder.Create()
            .BinaryTable(
                new BinaryTableBuilder().AddVariable("COMPRESSED", new[] { bytes }),
                header => header
                    .Logical("ZTABLE", true)
                    .Integer("ZNAXIS1", 8)
                    .Integer("ZNAXIS2", 1)
                    .Integer("ZPCOUNT", payload.Length)
                    .Integer("ZTILELEN", 1)
                    .Text("ZFORM1", "1PJ")
                    .Text("ZCTYP1", "NOCOMPRESS"))
            .Build();
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
