using System.Collections.Frozen;

namespace CFITS;

/// <summary>One header-and-data unit.</summary>
public sealed class FitsHdu
{
    private static readonly FrozenDictionary<string, FitsHduKind> ExtensionKinds =
        new Dictionary<string, FitsHduKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["IMAGE"] = FitsHduKind.Image,
            ["TABLE"] = FitsHduKind.AsciiTable,
            ["BINTABLE"] = FitsHduKind.BinaryTable,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private FitsHdu(int index, FitsHduKind kind, FitsHeader header, ReadOnlyMemory<byte> raw, long paddedDataLength, ReadOnlyMemory<byte> headerBytes, FitsImage? image, FitsTable? table, FitsGroups? groups, FitsIntegrity integrity)
    {
        Index = index;
        Kind = kind;
        Header = header;
        RawData = raw;
        PaddedDataLength = paddedDataLength;
        HeaderBytes = headerBytes;
        Image = image;
        Table = table;
        Groups = groups;
        Integrity = integrity;
    }

    public int Index { get; }

    public FitsHduKind Kind { get; }

    public FitsHeader Header { get; }

    public ReadOnlyMemory<byte> RawData { get; }

    private ReadOnlyMemory<byte> HeaderBytes { get; }

    internal int HeaderBytesLength => HeaderBytes.Length;
    internal long PaddedDataLength { get; }

    public FitsImage? Image { get; }

    public FitsTable? Table { get; }

    public FitsGroups? Groups { get; }

    public FitsIntegrity Integrity { get; }

    internal static FitsHdu Create(int index, FitsHeader header, ReadOnlyMemory<byte> raw, long paddedDataLength, HduShape shape, FitsScaleMode mode, ReadOnlyMemory<byte> headerBytes, ReadOnlyMemory<byte> dataBytes)
    {
        var kind = KindOf(shape);
        FitsImage? image = kind == FitsHduKind.Image ? FitsImage.Create(header, raw, shape, mode) : null;
        FitsTable? table = kind is FitsHduKind.AsciiTable or FitsHduKind.BinaryTable
            ? FitsTable.Create(header, raw, kind == FitsHduKind.AsciiTable, shape, mode)
            : null;
        if (table is not null && TileCompression.IsCompressedImage(header))
        {
            image = TileCompression.Decompress(header, table, mode);
            kind = FitsHduKind.Image;
        }
        else if (table is not null && TableCompression.IsCompressedTable(header))
        {
            table = TableCompression.Decompress(header, table, mode);
        }
        FitsGroups? groups = kind == FitsHduKind.RandomGroups ? FitsGroups.Create(header, raw, shape, mode) : null;
        bool uncompressed = image is not null && header.TryGetCard("ZDATASUM", out _);
        var integrity = FitsChecksum.Inspect(headerBytes.Span, dataBytes.Span, header, uncompressed ? image!.Stored.Span : default, uncompressed);
        return new FitsHdu(index, kind, header, raw, paddedDataLength, headerBytes, image, table, groups, integrity);
    }

    public FitsHduSnapshot Snapshot() => FitsHduSnapshot.Share(Index, Kind, HeaderBytes, RawData);

    private static FitsHduKind KindOf(HduShape shape)
    {
        if (shape.Groups)
        {
            return FitsHduKind.RandomGroups;
        }

        if (shape.Extension is null)
        {
            return FitsHduKind.Image;
        }

        return ExtensionKinds.TryGetValue(shape.Extension.Trim(), out FitsHduKind kind)
            ? kind
            : FitsHduKind.Unknown;
    }
}
