using System.Runtime.InteropServices;

namespace CFITS;

/// <summary>
/// An owned snapshot of an HDU that can be retained after its document is disposed.
/// When the header and payload are adjacent slices of one file buffer, the snapshot
/// keeps that buffer instead of copying both regions again.
/// </summary>
public sealed class FitsHduSnapshot
{
    private static readonly byte[] Padding = new byte[FitsBlock.Size];
    private readonly byte[] _bytes;
    private readonly int _origin;
    private readonly int _headerLength;
    private readonly int _dataLength;
    private FitsHeader? _header;

    internal FitsHduSnapshot(int index, FitsHduKind kind, ReadOnlySpan<byte> header, ReadOnlySpan<byte> data)
    {
        Index = index;
        Kind = kind;
        _headerLength = header.Length;
        _dataLength = data.Length;
        _bytes = new byte[header.Length + data.Length];
        header.CopyTo(_bytes);
        data.CopyTo(_bytes.AsSpan(header.Length));
    }

    private FitsHduSnapshot(int index, FitsHduKind kind, byte[] bytes, int origin, int headerLength, int dataLength)
    {
        Index = index;
        Kind = kind;
        _bytes = bytes;
        _origin = origin;
        _headerLength = headerLength;
        _dataLength = dataLength;
    }

    public int Index { get; }

    public FitsHduKind Kind { get; }

    internal static FitsHduSnapshot Share(int index, FitsHduKind kind, ReadOnlyMemory<byte> header, ReadOnlyMemory<byte> data)
    {
        if (MemoryMarshal.TryGetArray(header, out ArraySegment<byte> headerSegment)
            && MemoryMarshal.TryGetArray(data, out ArraySegment<byte> dataSegment)
            && headerSegment.Array is byte[] shared
            && ReferenceEquals(shared, dataSegment.Array)
            && headerSegment.Offset + header.Length == dataSegment.Offset)
        {
            return new FitsHduSnapshot(index, kind, shared, headerSegment.Offset, header.Length, data.Length);
        }

        return new FitsHduSnapshot(index, kind, header.Span, data.Span);
    }

    public FitsHduSnapshot WithIndex(int index) =>
        new(index, Kind, _bytes, _origin, _headerLength, _dataLength);

    public ReadOnlyMemory<byte> HeaderBytes => _bytes.AsMemory(_origin, _headerLength);

    public ReadOnlyMemory<byte> DataBytes => _bytes.AsMemory(_origin + _headerLength, _dataLength);

    public ReadOnlyMemory<byte> RawBytes => _bytes.AsMemory(_origin, _headerLength + _dataLength);

    public FitsHeader Header => _header ??= FitsHduStream.CreateHeader(CopyHeader(), _headerLength, Index);

    public void Write(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        stream.Write(_bytes.AsSpan(_origin, _headerLength));
        stream.Write(_bytes.AsSpan(_origin + _headerLength, _dataLength));
        long padding = FitsBlock.Pad(_dataLength) - _dataLength;
        if (padding > 0)
        {
            stream.Write(Padding.AsSpan(0, (int)padding));
        }
    }

    private byte[] CopyHeader()
    {
        byte[] copy = new byte[_headerLength];
        _bytes.AsSpan(_origin, _headerLength).CopyTo(copy);
        return copy;
    }
}
