namespace CFITS.Benchmarks;

/// <summary>
/// Targeted scenarios for BOYB (bring your own buffer) image reads into preallocated spans.
/// </summary>
internal static class BoybBenchmarks
{
    private static readonly float[] Image1024Buffer = new float[1024 * 1024];
    private static readonly short[] Image256Buffer = new short[256 * 256];
    private static short[]? _riceBuffer;
    private static object? _sink;

    public static IReadOnlyList<TargetedScenario> CreateScenarios(string data)
    {
        string image256 = Path.Combine(data, "image256.fits");
        string image1024 = Path.Combine(data, "image1024.fits");
        string rice = Path.Combine(data, "rice.fits");
        EnsureRiceBuffer(rice);

        return
        [
            new("boyb_image256_short", "boyb", () => _sink = SumBoybShort(Image256Buffer.AsSpan(), image256)),
            new("boyb_image1024_float", "boyb", () => _sink = SumBoybFloat(Image1024Buffer.AsSpan(), image1024)),
            new("boyb_rice_short", "boyb", () => _sink = SumBoybShort(_riceBuffer!.AsSpan(), rice, hduIndex: 1)),
            new("boyb_describe_image1024", "boyb", () => DescribeImage1024(image1024)),
        ];
    }

    private static void EnsureRiceBuffer(string ricePath)
    {
        if (_riceBuffer is not null)
        {
            return;
        }

        using var stream = File.OpenRead(ricePath);
        FitsImageLayout layout = FitsImage.Describe(stream, hduIndex: 1);
        _riceBuffer = new short[layout.PixelCount];
    }

    private static double SumBoybFloat(Span<float> destination, string path, int hduIndex = 0)
    {
        using var stream = File.OpenRead(path);
        FitsImage.Read(stream, destination, hduIndex);
        double sum = 0;
        for (int index = 0; index < destination.Length; index++)
        {
            sum += destination[index];
        }

        return sum;
    }

    private static double SumBoybShort(Span<short> destination, string path, int hduIndex = 0)
    {
        using var stream = File.OpenRead(path);
        FitsImage.Read(stream, destination, hduIndex);
        double sum = 0;
        for (int index = 0; index < destination.Length; index++)
        {
            sum += destination[index];
        }

        return sum;
    }

    private static void DescribeImage1024(string path)
    {
        using var stream = File.OpenRead(path);
        FitsImageLayout layout = FitsImage.Describe(stream);
        _sink = layout.PixelCount;
    }
}
