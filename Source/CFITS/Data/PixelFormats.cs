namespace CFITS.Data;

internal static class PixelFormats
{
    private static readonly IPixelFormat?[] ByBitpixIndex = CreateTable();

    public static int Width(int bitpix) => Resolve(bitpix).Width;

    public static IPixelFormat Resolve(int bitpix)
    {
        int index = bitpix + 64;
        if ((uint)index < (uint)ByBitpixIndex.Length && ByBitpixIndex[index] is IPixelFormat format)
        {
            return format;
        }

        throw new FitsException($"BITPIX {bitpix} is not a legal FITS value.", new FitsLocation(Keyword: "BITPIX"));
    }

    private static IPixelFormat?[] CreateTable()
    {
        var table = new IPixelFormat?[129];
        table[8 + 64] = new PixelFormat<byte>(8);
        table[16 + 64] = new PixelFormat<short>(16);
        table[32 + 64] = new PixelFormat<int>(32);
        table[64 + 64] = new PixelFormat<long>(64);
        table[-32 + 64] = new PixelFormatReal<float>(-32);
        table[-64 + 64] = new PixelFormatReal<double>(-64);
        return table;
    }
}
