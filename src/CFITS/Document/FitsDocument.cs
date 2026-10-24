namespace CFITS;

/// <summary>One header-and-data unit.</summary>
public sealed class FitsHdu
{
    private FitsHdu(int index, FitsHduKind kind, FitsHeader header, ReadOnlyMemory<byte> raw, FitsImage? image, FitsTable? table, FitsGroups? groups, FitsIntegrity integrity)
    {
        Index = index;
        Kind = kind;
        Header = header;
        RawData = raw;
        Image = image;
        Table = table;
        Groups = groups;
        Integrity = integrity;
    }

    public int Index { get; }

    public FitsHduKind Kind { get; }

    public FitsHeader Header { get; }

    public ReadOnlyMemory<byte> RawData { get; }

    public FitsImage? Image { get; }

    public FitsTable? Table { get; }

    public FitsGroups? Groups { get; }

    public FitsIntegrity Integrity { get; }

    internal static FitsHdu Create(int index, FitsHeader header, ReadOnlyMemory<byte> raw, HduShape shape, FitsScaleMode mode, ReadOnlySpan<byte> headerBytes, ReadOnlySpan<byte> dataBytes)
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
        var integrity = FitsChecksum.Inspect(headerBytes, dataBytes, header, uncompressed ? image!.Stored.Span : default, uncompressed);
        return new FitsHdu(index, kind, header, raw, image, table, groups, integrity);
    }

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

        return shape.Extension.Trim() switch
        {
            "IMAGE" => FitsHduKind.Image,
            "TABLE" => FitsHduKind.AsciiTable,
            "BINTABLE" => FitsHduKind.BinaryTable,
            _ => FitsHduKind.Unknown,
        };
    }
}

/// <summary>
/// A FITS file loaded for random access. This is the document API:
/// open a file, then ask for an HDU, a keyword, or a column.
/// </summary>
public sealed class FitsDocument : IDisposable
{
    private readonly List<FitsHdu> _hdus;
    private readonly byte[]? _owned;
    private bool _disposed;

    private FitsDocument(List<FitsHdu> hdus, ReadOnlyMemory<byte> special, byte[]? owned)
    {
        _hdus = hdus;
        SpecialRecords = special;
        _owned = owned;
    }

    public IReadOnlyList<FitsHdu> Hdus
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _hdus;
        }
    }

    public ReadOnlyMemory<byte> SpecialRecords { get; }

    public static FitsDocument Open(string path, FitsOpenOptions? options = null) =>
        Parse(File.ReadAllBytes(path), options);

    public static FitsDocument Open(Stream stream, FitsOpenOptions? options = null)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Parse(buffer.ToArray(), options);
    }

    public static FitsDocument Parse(ReadOnlyMemory<byte> data, FitsOpenOptions? options = null)
    {
        options ??= new FitsOpenOptions();
        var owned = data.ToArray();
        var hdus = new List<FitsHdu>();
        var physical = new List<CardRecord>();
        ReadOnlyMemory<byte> special = ReadOnlyMemory<byte>.Empty;
        var reader = new FitsReader(owned);
        int headerOffset = 0;
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case FitsToken.Hdu:
                    physical = [];
                    headerOffset = reader.HeaderOffset;
                    break;
                case FitsToken.Card:
                    physical.Add(CardRecord.FromView(reader.Card));
                    break;
                case FitsToken.EndHeader:
                    var header = FitsHeader.FromPhysical(physical, reader.HduIndex);
                    int headerLength = (int)reader.DataOffset - headerOffset;
                    var shape = DataLayout.Measure(owned.AsSpan(headerOffset, headerLength), reader.HduIndex);
                    long padded = FitsBlock.Pad(reader.DataLength);
                    int available = owned.Length - (int)reader.DataOffset;
                    int dataBytes = (int)Math.Min(padded, Math.Max(0, available));
                    var raw = owned.AsMemory((int)reader.DataOffset, (int)reader.DataLength);
                    var hdu = FitsHdu.Create(reader.HduIndex, header, raw, shape, options.ScaleMode, owned.AsSpan(headerOffset, headerLength), owned.AsSpan((int)reader.DataOffset, dataBytes));
                    if (options.VerifyChecksum && !hdu.Integrity.IsValid)
                    {
                        throw new FitsException("The HDU checksum does not match.", new FitsLocation(reader.HduIndex));
                    }

                    hdus.Add(hdu);
                    break;
                case FitsToken.SpecialRecords:
                    special = owned.AsMemory((int)reader.SpecialOffset, reader.SpecialLength).ToArray();
                    break;
            }
        }

        if (hdus.Count == 0)
        {
            throw new FitsException("The file does not contain a header.", new FitsLocation());
        }

        return new FitsDocument(hdus, special, owned);
    }

    public void Write(Stream stream)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (var hdu in _hdus)
        {
            hdu.Header.Write(stream);
            stream.Write(hdu.RawData.Span);
            WriteZeros(stream, FitsBlock.Pad(hdu.RawData.Length) - hdu.RawData.Length);
        }

        if (!SpecialRecords.IsEmpty)
        {
            stream.Write(SpecialRecords.Span);
        }
    }

    public void Save(string path)
    {
        using var stream = File.Create(path);
        Write(stream);
    }

    public byte[] ToArray()
    {
        using var stream = new MemoryStream();
        Write(stream);
        return stream.ToArray();
    }

    public void Dispose() => _disposed = true;

    private static void WriteZeros(Stream stream, long count)
    {
        Span<byte> zeros = stackalloc byte[FitsBlock.CardSize];
        while (count > 0)
        {
            int size = (int)Math.Min(zeros.Length, count);
            stream.Write(zeros[..size]);
            count -= size;
        }
    }
}
