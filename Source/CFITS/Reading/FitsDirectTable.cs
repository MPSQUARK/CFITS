using System.Buffers;

namespace CFITS;

/// <summary>
/// Opens one ASCII or binary table over a rented payload buffer.
/// Disposing the opener returns that buffer; copied cell values stay valid.
/// </summary>
public sealed class FitsDirectTable : IDisposable
{
    private byte[]? _rented;

    private FitsDirectTable(byte[] rented, FitsTable table)
    {
        _rented = rented;
        Table = table;
    }

    public FitsTable Table { get; }

    public static bool TryOpen(
        Stream stream,
        int hduIndex,
        string? extensionName,
        FitsOpenOptions options,
        out FitsDirectTable? opened)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(options);
        opened = null;
        if (options.VerifyChecksum || !stream.CanSeek)
        {
            return false;
        }

        long origin = stream.Position;
        if (!FitsHduStream.TrySelect(stream, hduIndex, extensionName, image: false, out FitsHduStream.Opened selected, out FitsHeader? header))
        {
            return false;
        }

        byte[]? rented = null;
        try
        {
            if (selected.Shape.DataBytes > int.MaxValue)
            {
                stream.Position = origin;
                return false;
            }

            int length = (int)selected.Shape.DataBytes;
            rented = ArrayPool<byte>.Shared.Rent(Math.Max(length, 1));
            if (length > 0)
            {
                stream.ReadExactly(rented.AsSpan(0, length));
            }

            bool ascii = string.Equals(selected.Shape.Extension, "TABLE", StringComparison.OrdinalIgnoreCase);
            FitsTable table = FitsTable.Create(
                header!,
                rented.AsMemory(0, length),
                ascii,
                selected.Shape,
                options.ScaleMode);
            FitsHduStream.SkipPadding(stream, selected.Shape);
            FitsHduStream.ValidateRemainder(stream);
            opened = new FitsDirectTable(rented, table);
            rented = null;
            return true;
        }
        catch
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }

            stream.Position = origin;
            throw;
        }
    }

    public void Dispose()
    {
        byte[]? rented = _rented;
        if (rented is null)
        {
            return;
        }

        _rented = null;
        ArrayPool<byte>.Shared.Return(rented);
    }
}
