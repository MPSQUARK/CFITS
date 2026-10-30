using System.Diagnostics;

namespace CFITS.Benchmarks;

internal static class HeaderParserBenchmarks
{
    public static void Run()
    {
        byte[] continuation = CreateContinuationHeader();
        byte[] commentary = CreateCommentaryHeader();
        Measure("header_parser_continuation", continuation);
        Measure("header_parser_commentary", commentary);
    }

    private static void Measure(string name, byte[] image)
    {
        const int iterations = 16;
        for (int index = 0; index < 4; index++)
        {
            Parse(image);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        for (int index = 0; index < iterations; index++)
        {
            Parse(image);
        }

        stopwatch.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Console.WriteLine($"{name},mean_ns={stopwatch.Elapsed.TotalNanoseconds / iterations:G17},allocated_bytes={allocated / iterations}");
    }

    private static void Parse(byte[] image)
    {
        using FitsDocument document = FitsDocument.Parse(image);
        GC.KeepAlive(document.Hdus[0].Header.Cards.Count);
    }

    private static byte[] CreateContinuationHeader() =>
        FitsDocumentBuilder.Create()
            .Image(Array.Empty<int>(), Array.Empty<byte>(), header => header
                .Integer("BEFORE", 1)
                .Text("LONGSTR", new string('x', 1_024), "continued")
                .Integer("AFTER", 2)
                .Comment("kept"))
            .Build()
            .ToArray();

    private static byte[] CreateCommentaryHeader() =>
        FitsDocumentBuilder.Create()
            .Image(Array.Empty<int>(), Array.Empty<byte>(), header => header
                .Comment("commentary")
                .History("history")
                .Blank("blank"))
            .Build()
            .ToArray();
}
