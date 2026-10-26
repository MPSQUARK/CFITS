namespace CFITS.Data;

/// <summary>
/// Legal FITS BITPIX values. A fixed table indexed by <c>bitpix + 64</c> gives
/// constant-time membership and byte width without hashing.
/// </summary>
internal static class FitsBitpix
{
    private static readonly bool[] Legal = BuildLegal();
    private static readonly byte[] ByteWidthByIndex = BuildByteWidths();

    public static bool IsLegal(int bitpix)
    {
        int index = bitpix + 64;
        return (uint)index < (uint)Legal.Length && Legal[index];
    }

    public static int ByteWidth(int bitpix)
    {
        int index = bitpix + 64;
        if ((uint)index >= (uint)ByteWidthByIndex.Length)
        {
            ThrowIllegal(bitpix);
        }

        byte width = ByteWidthByIndex[index];
        if (width == 0)
        {
            ThrowIllegal(bitpix);
        }

        return width;
    }

    private static void ThrowIllegal(int bitpix) =>
        throw new FitsException($"BITPIX {bitpix} is not a legal FITS value.", new FitsLocation(Keyword: "BITPIX"));

    private static bool[] BuildLegal()
    {
        var legal = new bool[129];
        ReadOnlySpan<int> values = [8, 16, 32, 64, -32, -64];
        for (int index = 0; index < values.Length; index++)
        {
            legal[values[index] + 64] = true;
        }

        return legal;
    }

    private static byte[] BuildByteWidths()
    {
        var widths = new byte[129];
        ReadOnlySpan<(int Bitpix, byte Width)> pairs =
        [
            (8, 1),
            (16, 2),
            (32, 4),
            (64, 8),
            (-32, 4),
            (-64, 8),
        ];

        for (int index = 0; index < pairs.Length; index++)
        {
            (int bitpix, byte width) = pairs[index];
            widths[bitpix + 64] = width;
        }

        return widths;
    }
}
