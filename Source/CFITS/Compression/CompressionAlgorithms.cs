using System.Collections.Frozen;

namespace CFITS.Compression;

/// <summary>
/// Normalized FITS compression algorithm names (ZCMPTYPE / ZCTYPn).
/// </summary>
internal static class CompressionAlgorithms
{
    private static readonly FrozenDictionary<string, CompressionAlgorithmId> TileImageByName =
        new Dictionary<string, CompressionAlgorithmId>(StringComparer.Ordinal)
        {
            ["GZIP_1"] = CompressionAlgorithmId.Gzip1,
            ["GZIP_2"] = CompressionAlgorithmId.Gzip2,
            ["RICE_1"] = CompressionAlgorithmId.Rice1,
            ["RICE_ONE"] = CompressionAlgorithmId.RiceOne,
            ["HCOMPRESS_1"] = CompressionAlgorithmId.Hcompress1,
            ["PLIO_1"] = CompressionAlgorithmId.Plio1,
            ["NOCOMPRESS"] = CompressionAlgorithmId.Nocompress,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenSet<string> TileMask = new[]
    {
        "NOCOMPRESS",
        "GZIP_1",
        "GZIP_2",
        "RICE_1",
        "RICE_ONE",
        "PLIO_1",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> TableTile = new[]
    {
        "NOCOMPRESS",
        "GZIP_1",
        "GZIP_2",
        "RICE_1",
        "RICE_ONE",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> QuantizeModes = new[]
    {
        "NO_DITHER",
        "SUBTRACTIVE_DITHER_1",
        "SUBTRACTIVE_DITHER_2",
    }.ToFrozenSet(StringComparer.Ordinal);

    public static string Normalize(string algorithm) =>
        algorithm.Trim().Replace(' ', '_').ToUpperInvariant();

    public static bool TryResolveTileImage(string normalized, out CompressionAlgorithmId id) =>
        TileImageByName.TryGetValue(normalized, out id);

    public static CompressionAlgorithmId ResolveTileImage(string normalized) =>
        TryResolveTileImage(normalized, out CompressionAlgorithmId id)
            ? id
            : throw new InvalidOperationException($"Unknown tile image algorithm '{normalized}'.");

    public static bool IsTileImage(string normalized) => TileImageByName.ContainsKey(normalized);

    public static bool IsTileMask(string normalized) => TileMask.Contains(normalized);

    public static bool IsTableTile(string normalized) => TableTile.Contains(normalized);

    public static bool IsGzip(CompressionAlgorithmId id) =>
        id is CompressionAlgorithmId.Gzip1 or CompressionAlgorithmId.Gzip2;

    public static bool IsGzip(string normalized) =>
        normalized is "GZIP_1" or "GZIP_2";

    public static bool IsQuantizeMode(string normalized) => QuantizeModes.Contains(normalized);
}
