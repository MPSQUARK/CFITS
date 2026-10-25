namespace CFITS;

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
        ParseOwned(File.ReadAllBytes(path), options ?? new FitsOpenOptions());

    public static FitsDocument Open(Stream stream, FitsOpenOptions? options = null)
    {
        MemoryStream buffer;
        try
        {
            long remaining = stream.CanSeek ? stream.Length - stream.Position : 0;
            int capacity = remaining is >= 0 and <= int.MaxValue ? (int)remaining : 0;
            buffer = new MemoryStream(capacity);
        }
        catch (NotSupportedException)
        {
            buffer = new MemoryStream();
        }

        using (buffer)
        {
            stream.CopyTo(buffer);
            if (buffer.TryGetBuffer(out ArraySegment<byte> segment)
                && segment.Array is byte[] owned
                && segment.Offset == 0
                && segment.Count == buffer.Length)
            {
                return ParseOwned(owned, options ?? new FitsOpenOptions());
            }

            return ParseOwned(buffer.ToArray(), options ?? new FitsOpenOptions());
        }
    }

    public static FitsDocument Parse(ReadOnlyMemory<byte> data, FitsOpenOptions? options = null)
    {
        options ??= new FitsOpenOptions();
        return ParseOwned(data.ToArray(), options);
    }

    private static FitsDocument ParseOwned(byte[] owned, FitsOpenOptions options)
    {
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
                    long padded = FitsBlock.Pad(reader.DataLength);
                    int available = owned.Length - (int)reader.DataOffset;
                    int dataBytes = (int)Math.Min(padded, Math.Max(0, available));
                    var raw = owned.AsMemory((int)reader.DataOffset, (int)reader.DataLength);
                    var hdu = FitsHdu.Create(reader.HduIndex, header, raw, reader.Shape, options.ScaleMode, owned.AsSpan(headerOffset, headerLength), owned.AsSpan((int)reader.DataOffset, dataBytes));
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
