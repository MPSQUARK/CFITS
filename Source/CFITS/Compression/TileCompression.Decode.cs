namespace CFITS.Compression;

internal static partial class TileCompression
{
    private static TileBytes DecodeGzip(byte[] compressed, int pixels, int bytePix, bool gzip2)
    {
        byte[] decoded = Gunzip(compressed);
        if (pixels <= 0 || decoded.Length % pixels != 0)
        {
            throw new FitsException("A GZIP tile does not contain a whole number of encoded pixels.", new FitsLocation());
        }

        int encodedWidth = decoded.Length / pixels;
        byte[] plain = gzip2 ? Unshuffle(decoded, encodedWidth) : decoded;
        return new TileBytes(plain, encodedWidth);
    }

    private static TileBytes DecodeRice(byte[] compressed, int pixels, int bytePix, int blockSize, int width, int smooth)
    {
        uint[] values = Rice.Decompress(compressed, pixels, bytePix, blockSize);
        return new TileBytes(Pack(values, pixels, bytePix), bytePix);
    }

    private static TileBytes DecodePlio(byte[] compressed, int pixels, int bytePix, int blockSize, int width, int smooth)
    {
        int[] values = Plio.Decompress(compressed, pixels);
        var bytes = new byte[pixels * bytePix];
        int offset = 0;
        for (int index = 0; index < pixels; index++)
        {
            WriteInteger(bytes.AsSpan(offset, bytePix), values[index]);
            offset += bytePix;
        }

        return new TileBytes(bytes, bytePix);
    }

    private static TileBytes DecodeHcompress(byte[] compressed, int pixels, int bytePix, int blockSize, int width, int smooth)
    {
        int[] values = HCompress.Decompress(compressed, smooth);
        if (values.Length != pixels)
        {
            throw new FitsException("An H-compress tile does not match the image tile.", new FitsLocation());
        }

        var bytes = new byte[pixels * 4];
        int offset = 0;
        for (int index = 0; index < pixels; index++)
        {
            WriteInteger(bytes.AsSpan(offset, 4), values[index]);
            offset += 4;
        }

        return new TileBytes(bytes, 4);
    }

    private static TileBytes DecodeNocompress(byte[] compressed, int pixels, int bytePix, int blockSize, int width, int smooth) =>
        new TileBytes(RequireLength(compressed, checked(pixels * bytePix)), bytePix);
}
