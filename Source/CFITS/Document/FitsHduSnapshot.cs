namespace CFITS;

/// <summary>
/// An owned snapshot of an HDU that can be retained after its document is disposed.
/// </summary>
public sealed class FitsHduSnapshot
{
    private static readonly byte[] Padding = new byte[FitsBlock.Size];
    private readonly byte[] _bytes;
    private readonly int _headerLength;

    internal FitsHduSnapshot(int index, FitsHduKind kind, ReadOnlySpan<byte> header, ReadOnlySpan<byte> data)
    {
        Index = index;
        Kind = kind;
        _headerLength = header.Length;
        _bytes = new byte[header.Length + data.Length];
        header.CopyTo(_bytes);
        data.CopyTo(_bytes.AsSpan(header.Length));
    }

    private FitsHduSnapshot(int index, FitsHduKind kind, byte[] bytes, int headerLength)
    {
        Index = index;
        Kind = kind;
        _bytes = bytes;
        _headerLength = headerLength;
    }

    public int Index { get; }

    public FitsHduKind Kind { get; }

    public FitsHduSnapshot WithIndex(int index) =>
        new(index, Kind, _bytes, _headerLength);

    public ReadOnlyMemory<byte> HeaderBytes => _bytes.AsMemory(0, _headerLength);

    public ReadOnlyMemory<byte> DataBytes => _bytes.AsMemory(_headerLength);

    public ReadOnlyMemory<byte> RawBytes => _bytes;

    public void Write(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        stream.Write(_bytes.AsSpan(0, _headerLength));
        stream.Write(_bytes.AsSpan(_headerLength));
        long padding = FitsBlock.Pad(_bytes.Length - _headerLength) - (_bytes.Length - _headerLength);
        if (padding > 0)
        {
            stream.Write(Padding.AsSpan(0, (int)padding));
        }
    }
}
