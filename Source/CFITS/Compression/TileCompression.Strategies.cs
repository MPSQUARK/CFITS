namespace CFITS.Compression;

internal static partial class TileCompression
{
    private interface ITileDecodeStrategy
    {
        TileBytes Decode(byte[] compressed, int pixels, int bytePix, int blockSize, int width, int smooth);
    }

    private static class TileDecodeStrategies
    {
        private static readonly ITileDecodeStrategy[] ById = Build();

        internal static ITileDecodeStrategy For(CompressionAlgorithmId id) => ById[(int)id];

        private static ITileDecodeStrategy[] Build()
        {
            var strategies = new ITileDecodeStrategy[7];
            strategies[(int)CompressionAlgorithmId.Gzip1] = new Gzip1TileDecodeStrategy();
            strategies[(int)CompressionAlgorithmId.Gzip2] = new Gzip2TileDecodeStrategy();
            strategies[(int)CompressionAlgorithmId.Rice1] = RiceTileDecodeStrategy.Instance;
            strategies[(int)CompressionAlgorithmId.RiceOne] = RiceTileDecodeStrategy.Instance;
            strategies[(int)CompressionAlgorithmId.Plio1] = PlioTileDecodeStrategy.Instance;
            strategies[(int)CompressionAlgorithmId.Hcompress1] = HcompressTileDecodeStrategy.Instance;
            strategies[(int)CompressionAlgorithmId.Nocompress] = NocompressTileDecodeStrategy.Instance;
            return strategies;
        }
    }

    private sealed class Gzip1TileDecodeStrategy : ITileDecodeStrategy
    {
        public TileBytes Decode(byte[] compressed, int pixels, int bytePix, int blockSize, int width, int smooth) =>
            DecodeGzip(compressed, pixels, bytePix, gzip2: false);
    }

    private sealed class Gzip2TileDecodeStrategy : ITileDecodeStrategy
    {
        public TileBytes Decode(byte[] compressed, int pixels, int bytePix, int blockSize, int width, int smooth) =>
            DecodeGzip(compressed, pixels, bytePix, gzip2: true);
    }

    private sealed class RiceTileDecodeStrategy : ITileDecodeStrategy
    {
        internal static readonly RiceTileDecodeStrategy Instance = new();

        public TileBytes Decode(byte[] compressed, int pixels, int bytePix, int blockSize, int width, int smooth) =>
            DecodeRice(compressed, pixels, bytePix, blockSize, width, smooth);
    }

    private sealed class PlioTileDecodeStrategy : ITileDecodeStrategy
    {
        internal static readonly PlioTileDecodeStrategy Instance = new();

        public TileBytes Decode(byte[] compressed, int pixels, int bytePix, int blockSize, int width, int smooth) =>
            DecodePlio(compressed, pixels, bytePix, blockSize, width, smooth);
    }

    private sealed class HcompressTileDecodeStrategy : ITileDecodeStrategy
    {
        internal static readonly HcompressTileDecodeStrategy Instance = new();

        public TileBytes Decode(byte[] compressed, int pixels, int bytePix, int blockSize, int width, int smooth) =>
            DecodeHcompress(compressed, pixels, bytePix, blockSize, width, smooth);
    }

    private sealed class NocompressTileDecodeStrategy : ITileDecodeStrategy
    {
        internal static readonly NocompressTileDecodeStrategy Instance = new();

        public TileBytes Decode(byte[] compressed, int pixels, int bytePix, int blockSize, int width, int smooth) =>
            DecodeNocompress(compressed, pixels, bytePix, blockSize, width, smooth);
    }
}
