namespace CFITS;

/// <summary>
/// An owned snapshot of an HDU that can be retained after its document is disposed.
/// </summary>
public sealed class FitsHduSnapshot
{
    private readonly byte[] _header;
    private readonly byte[] _data;

    internal FitsHduSnapshot(int index, FitsHduKind kind, ReadOnlySpan<byte> header, ReadOnlySpan<byte> data)
    {
        Index = index;
        Kind = kind;
        _header = header.ToArray();
        _data = data.ToArray();
    }

    public int Index { get; }

    public FitsHduKind Kind { get; }

    public FitsHduSnapshot WithIndex(int index) =>
        new(index, Kind, _header, _data);

    public ReadOnlyMemory<byte> HeaderBytes => _header;

    public ReadOnlyMemory<byte> DataBytes => _data;

    public ReadOnlyMemory<byte> RawBytes
    {
        get
        {
            var bytes = new byte[_header.Length + _data.Length];
            _header.CopyTo(bytes, 0);
            _data.CopyTo(bytes, _header.Length);
            return bytes;
        }
    }

    public void Write(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        stream.Write(_header);
        stream.Write(_data);
        long padding = FitsBlock.Pad(_data.Length) - _data.Length;
        if (padding > 0)
        {
            stream.Write(new byte[(int)padding]);
        }
    }
}
