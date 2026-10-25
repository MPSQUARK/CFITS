using System.Diagnostics;
using System.Text;

using System.Numerics;

namespace CFITS.Tests;

public sealed class ConstructedRoundTripTests
{
    [Fact]
    public void BuiltTablesAndGroupsMatchAstropy()
    {
        var table = new BinaryTableBuilder()
            .AddComplex("M", [new FitsComplex(1, 2), new FitsComplex(3, -4), new FitsComplex(5, 6)])
            .AddSingleComplex("C", [new FitsComplex(1.5, -2.5), new FitsComplex(0, 1), new FitsComplex(-3, 4)])
            .AddBits("X", 3, [[true, false, true], [false, true, false], [true, true, false]])
            .AddVariable("ARR", new int[][] { [1, 2, 3], [4], [] })
            .AddVariable("NAME", new[] { "ab", "cdef", "" })
            .AddVector("VEC", 6, Enumerable.Range(0, 18).ToArray(), [2, 3]);
        using var built = FitsDocumentBuilder.Create().BinaryTable(table, header => header.Text("EXTNAME", "BUILT")).Build();
        var binary = built.Hdus[1].Table!;
        Assert.Equal(new FitsComplex(1, 2), binary.Column("M").ReadComplex(0));
        Assert.Equal(new FitsComplex(3, -4), binary.Column("M").ReadComplex(1));
        var single = binary.Column("C").ReadComplex(0);
        Assert.Equal(1.5, single.Real);
        Assert.Equal(-2.5, single.Imaginary);
        Assert.Equal(1, binary.Column("X").ReadInteger(0, 0));
        Assert.Equal(0, binary.Column("X").ReadInteger(0, 1));
        Assert.Equal(3, binary.Column("ARR").Count(0));
        Assert.Equal(4, binary.Column("ARR").ReadInteger(1));
        Assert.Equal(0, binary.Column("ARR").Count(2));
        Assert.Equal("cdef", binary.Column("NAME").ReadString(1));
        Assert.Equal(new[] { 2, 3 }, binary.Column("VEC").Dimensions);
        Assert.Equal(0, binary.Column("VEC").ReadInteger(0, 0));
        Assert.Equal(5, binary.Column("VEC").ReadInteger(0, 5));
        Assert.Equal(17, binary.Column("VEC").ReadInteger(2, 5));
        CompareWithAstropy(built, "built-table.fits");

        using var groups = FitsDocumentBuilder.Create()
            .RandomGroups(-32, [3], 2, [1, 2, 3, 4], [0, 1, 2, 3, 4, 5], ["UU", "VV"])
            .Build();
        Assert.Equal(1, groups.Hdus[0].Groups!.ReadParameter(0, 0));
        Assert.Equal(4, groups.Hdus[0].Groups!.ReadParameter(1, 1));
        Assert.Equal(5, groups.Hdus[0].Groups!.ReadData(1, 2));
        CompareWithAstropy(groups, "built-groups.fits");
        using var ascii = FitsDocumentBuilder.Create().AsciiTable(new AsciiTableBuilder()
            .AddText("NAME", ["alpha"], 8)
            .AddReal("FLUX", [1.5e-7], 12, 3, 'E')
            .AddReal("DIST", [1.25e-4], 14, 4, 'D')).Build();
        double flux = ascii.Hdus[1].Table!.Column("FLUX").ReadReal(0);
        double distance = ascii.Hdus[1].Table!.Column("DIST").ReadReal(0);
        Assert.True(Math.Abs(flux - 1.5e-7) < 1e-12, $"FLUX {flux}");
        Assert.True(Math.Abs(distance - 1.25e-4) < 1e-12, $"DIST {distance}");
        CompareWithAstropy(ascii, "built-ascii.fits");
        using var fields = FitsDocumentBuilder.Create().BinaryTable(
            new BinaryTableBuilder().AddText("WORDS", 8, [["hello", "world"], ["alpha", "beta"]])).Build();
        Assert.Equal("hello", fields.Hdus[1].Table!.Column("WORDS").ReadString(0, 0));
        Assert.Equal("world", fields.Hdus[1].Table!.Column("WORDS").ReadString(0, 1));
        Assert.Equal("beta", fields.Hdus[1].Table!.Column("WORDS").ReadString(1, 1));
        CompareWithAstropy(fields, "built-fields.fits");

        var bits = new BinaryTableBuilder().AddVariableBits("BITS", [[true, false, true, true], [false], []]);
        using var variableBits = FitsDocumentBuilder.Create().BinaryTable(bits).Build();
        var column = variableBits.Hdus[1].Table!.Column("BITS");
        Assert.Equal(4, column.Count(0));
        Assert.Equal(1, column.ReadInteger(0, 0));
        Assert.Equal(0, column.ReadInteger(0, 1));
        Assert.Equal(1, column.ReadInteger(0, 3));
        Assert.Equal(1, column.Count(1));
        Assert.Equal(0, column.Count(2));

        using var scaled = FitsDocumentBuilder.Create().BinaryTable(
            new BinaryTableBuilder().AddSingleComplex("C", [new FitsComplex(1, 2)]),
            header => header.Real("TSCAL1", 2).Real("TZERO1", 10)).Build();
        var scaledValue = scaled.Hdus[1].Table!.Column("C").ReadComplex(0);
        Assert.Equal(12, scaledValue.Real);
        Assert.Equal(14, scaledValue.Imaginary);

        using var numerics = FitsDocumentBuilder.Create().BinaryTable(
            new BinaryTableBuilder().AddComplexValues("C", [new Complex(3, 4)])).Build();
        Assert.Equal(new Complex(3, 4), numerics.Hdus[1].Table!.Column("C").ReadComplexValue(0));

        using var repeated = FitsDocumentBuilder.Create().BinaryTable(new BinaryTableBuilder()
            .Add("L", new bool?[] { true, null, false })
            .AddVector("E", 2, new float[] { 1.5f, 2.5f, 3.5f, 4.5f, 5.5f, 6.5f }, [2])
            .AddVector("D", 2, new double[] { 3.5, 4.5, 1, 2, 3, 4 }, [2])).Build();
        Assert.True(repeated.Hdus[1].Table!.Columns[0].IsNull(1));
        Assert.Equal(2.5, repeated.Hdus[1].Table!.Column("E").ReadReal(0, 1));
        Assert.Equal(4.5, repeated.Hdus[1].Table!.Column("D").ReadReal(0, 1));
        using var vectors = FitsDocumentBuilder.Create().BinaryTable(new BinaryTableBuilder()
            .AddVector("E", 2, new float[] { 1.5f, 2.5f }, [2])
            .AddVector("D", 2, new double[] { 3.5, 4.5 }, [2])).Build();
        CompareWithAstropy(vectors, "built-vectors.fits");

        using var scaledGroups = FitsDocumentBuilder.Create()
            .RandomGroups(-32, [1], 1, [2], [4], ["UU"], header => header.Real("BSCALE", 2).Logical("EXTEND", false))
            .Build();
        Assert.Equal(8, scaledGroups.Hdus[0].Groups!.ReadData(0, 0));
        Assert.False(scaledGroups.Hdus[0].Header.GetBoolean("EXTEND"));
    }

    private static void CompareWithAstropy(FitsDocument document, string name)
    {
        var root = FindRoot();
        var directory = Path.Combine(Path.GetTempPath(), "cfits-built");
        Directory.CreateDirectory(directory);
        var fits = Path.Combine(directory, name);
        var dump = fits + ".json";
        document.Save(fits);
        File.WriteAllText(dump, OracleDump.Create(document));
        var python = PythonRuntime.Find(root);
        var oracle = Path.Combine(root, "Tools", "CFITS.PyScripts", "Oracles", "Oracle.py");
        var start = new ProcessStartInfo(python)
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(oracle);
        start.ArgumentList.Add("check-dump");
        start.ArgumentList.Add(fits);
        start.ArgumentList.Add(dump);
        using var process = Process.Start(start)!;
        var output = new StringBuilder();
        process.OutputDataReceived += (_, args) => output.AppendLine(args.Data);
        process.ErrorDataReceived += (_, args) => output.AppendLine(args.Data);
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
            if (File.Exists(Path.Combine(directory.FullName, "CFITS.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("The CFITS repository root was not found.");
    }
}
