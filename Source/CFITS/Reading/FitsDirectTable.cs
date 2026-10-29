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
        ReadOnlySpan<string> columns,
        out FitsDirectTable? opened) =>
        TryOpen(stream, hduIndex, extensionName, options, columns, requireColumns: true, materializeHeader: false, out opened, out _);

    public static bool TryOpen(
        Stream stream,
        int hduIndex,
        string? extensionName,
        FitsOpenOptions options,
        ReadOnlySpan<string> columns,
        bool requireColumns,
        out FitsDirectTable? opened,
        out FitsHeader? header) =>
        TryOpen(stream, hduIndex, extensionName, options, columns, requireColumns, materializeHeader: true, out opened, out header);

    private static bool TryOpen(
        Stream stream,
        int hduIndex,
        string? extensionName,
        FitsOpenOptions options,
        ReadOnlySpan<string> columns,
        bool requireColumns,
        bool materializeHeader,
        out FitsDirectTable? opened,
        out FitsHeader? header)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(options);
        opened = null;
        header = null;
        if (options.VerifyChecksum || !stream.CanSeek)
        {
            return false;
        }

        long origin = stream.Position;
        if (!FitsHduStream.TrySelect(stream, hduIndex, extensionName, image: false, out FitsHduStream.Opened selected))
        {
            return false;
        }

        byte[]? rented = null;
        try
        {
            if (selected.Shape.DataBytes > int.MaxValue)
            {
                stream.Position = origin;
                selected.Dispose();
                return false;
            }

            int length = (int)selected.Shape.DataBytes;
            rented = ArrayPool<byte>.Shared.Rent(Math.Max(length, 1));
            if (length > 0)
            {
                stream.ReadExactly(rented.AsSpan(0, length));
            }

            FitsTable table = DirectColumnLayout.Select(
                selected.Header,
                selected.Shape,
                rented.AsMemory(0, length),
                columns,
                options.ScaleMode,
                selected.Index,
                requireColumns);
            header = materializeHeader
                ? FitsHduStream.CreateHeader(selected.DetachBuffer(), selected.HeaderLength, selected.Index)
                : null;
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
        finally
        {
            selected.Dispose();
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
