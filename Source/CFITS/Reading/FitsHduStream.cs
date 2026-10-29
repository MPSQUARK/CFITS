using System.Buffers;

namespace CFITS;

/// <summary>
/// Reads one FITS header from a seekable stream and positions the stream at that HDU's data.
/// </summary>
internal static class FitsHduStream
{
    internal struct Opened : IDisposable
    {
        private byte[]? _buffer;

        public Opened(byte[] buffer, int headerLength, HduShape shape, int index)
        {
            _buffer = buffer;
            HeaderLength = headerLength;
            Shape = shape;
            Index = index;
        }

        public int HeaderLength { get; }

        public HduShape Shape { get; }

        public int Index { get; }

        public ReadOnlySpan<byte> Header => _buffer is null ? [] : _buffer.AsSpan(0, HeaderLength);

        public byte[] DetachBuffer()
        {
            byte[] buffer = _buffer ?? throw new InvalidOperationException("The header buffer was already released.");
            _buffer = null;
            return buffer;
        }

        public void Dispose()
        {
            byte[]? buffer = _buffer;
            if (buffer is null)
            {
                return;
            }

            _buffer = null;
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static bool TrySelect(
        Stream stream,
        int hduIndex,
        string? extensionName,
        bool image,
        out Opened opened,
        bool compressedImage = false)
    {
        opened = default;
        long origin = stream.Position;
        int index = 0;
        while (true)
        {
            if (!TryReadHeader(stream, index, out Opened candidate))
            {
                stream.Position = origin;
                return false;
            }

            ReadOnlySpan<byte> bytes = candidate.Header;
            bool compressed = IsCompressedImage(bytes);
            bool kind = compressedImage
                ? candidate.Shape.Extension == "BINTABLE" && compressed
                : image
                    ? candidate.Shape.Extension is null or "IMAGE"
                    : candidate.Shape.Extension is "TABLE" or "BINTABLE" && !compressed;
            bool name = extensionName is null || ExtensionName(bytes) == extensionName;
            bool indexMatches = hduIndex < 0 || index == hduIndex;
            if (kind && name && indexMatches)
            {
                opened = candidate;
                return true;
            }

            candidate.Dispose();
            if (hduIndex >= 0 && index == hduIndex)
            {
                stream.Position = origin;
                return false;
            }

            SkipData(stream, candidate.Shape);
            index++;
        }
    }

    public static void SkipPadding(Stream stream, HduShape shape)
    {
        long padded = FitsBlock.Pad(shape.DataBytes);
        long padding = padded - shape.DataBytes;
        if (padding > 0)
        {
            stream.Seek(padding, SeekOrigin.Current);
        }
    }

    public static void ValidateRemainder(Stream stream)
    {
        while (TryReadHeader(stream, 1, out Opened opened))
        {
            HduShape shape = opened.Shape;
            opened.Dispose();
            SkipData(stream, shape);
        }
    }

    public static double Scale(FitsHeader header, string keyword) =>
        header.TryGetCard(keyword, out FitsCard card) ? card.GetDouble() : 1;

    public static double Zero(FitsHeader header, string keyword) =>
        header.TryGetCard(keyword, out FitsCard card) ? card.GetDouble() : 0;

    public static long? Null(FitsHeader header, string keyword) =>
        header.TryGetCard(keyword, out FitsCard card) && card.Kind == FitsValueKind.Integer ? card.Integer : null;

    internal static bool TryReadPrimaryHeader(Stream stream, out FitsHeader? header)
    {
        header = null;
        if (!stream.CanSeek)
        {
            return false;
        }

        long origin = stream.Position;
        if (!TryReadHeader(stream, 0, out Opened opened))
        {
            stream.Position = origin;
            return false;
        }

        try
        {
            header = CreateHeader(opened.DetachBuffer(), opened.HeaderLength, 0);
            return header is not null;
        }
        finally
        {
            opened.Dispose();
            stream.Position = origin;
        }
    }

    private static bool TryReadHeader(Stream stream, int hduIndex, out Opened opened)
    {
        opened = default;
        long remaining = stream.Length - stream.Position;
        if (remaining == 0)
        {
            return false;
        }

        Span<byte> keyword = stackalloc byte[8];
        ReadExact(stream, keyword);
        stream.Seek(-keyword.Length, SeekOrigin.Current);
        bool primary = hduIndex == 0;
        bool simple = keyword.StartsWith("SIMPLE"u8);
        bool extension = keyword.StartsWith("XTENSION"u8);
        if (primary && !simple)
        {
            throw new FitsException("A FITS file must start with SIMPLE.", new FitsLocation(0, "SIMPLE", Position: stream.Position));
        }

        if (!primary && !extension)
        {
            if (remaining % FitsBlock.Size != 0)
            {
                throw new FitsException(
                    "Trailing bytes are not a whole number of 2880-byte records.",
                    new FitsLocation(hduIndex, Position: stream.Position));
            }

            return false;
        }

        byte[]? buffer = ArrayPool<byte>.Shared.Rent(FitsBlock.Size);
        int length = 0;
        try
        {
            bool ended = false;
            while (!ended)
            {
                if (buffer.Length < length + FitsBlock.Size)
                {
                    byte[] grown = ArrayPool<byte>.Shared.Rent(length + FitsBlock.Size);
                    buffer.AsSpan(0, length).CopyTo(grown);
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = grown;
                }

                ReadExact(stream, buffer.AsSpan(length, FitsBlock.Size));
                for (int offset = length; offset < length + FitsBlock.Size && !ended; offset += FitsBlock.CardSize)
                {
                    ReadOnlySpan<byte> name = buffer.AsSpan(offset, 8);
                    if (name.StartsWith("END"u8) && name.TrimEnd((byte)' ').Length == 3)
                    {
                        ended = true;
                    }
                }

                length += FitsBlock.Size;
            }

            HduShape shape = DataLayout.Measure(buffer.AsSpan(0, length), hduIndex, 0);
            opened = new Opened(buffer, length, shape, hduIndex);
            buffer = null;
            return true;
        }
        catch
        {
            if (buffer is not null)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            throw;
        }
    }

    private static void SkipData(Stream stream, HduShape shape) =>
        stream.Seek(FitsBlock.Pad(shape.DataBytes), SeekOrigin.Current);

    internal static FitsHeader CreateHeader(byte[] header, int length, int hduIndex)
    {
        var records = new List<CardRecord>();
        for (int offset = 0; offset + FitsBlock.CardSize <= length; offset += FitsBlock.CardSize)
        {
            FitsCardView view = FitsCardView.Parse(header.AsSpan(offset, FitsBlock.CardSize), offset);
            if (view.IsEnd)
            {
                break;
            }

            records.Add(CardRecord.FromImage(header.AsMemory(offset, FitsBlock.CardSize), view));
        }

        return FitsHeader.FromPhysical(records, hduIndex);
    }

    private static string? ExtensionName(ReadOnlySpan<byte> header)
    {
        for (int offset = 0; offset + FitsBlock.CardSize <= header.Length; offset += FitsBlock.CardSize)
        {
            FitsCardView view = FitsCardView.Parse(header.Slice(offset, FitsBlock.CardSize), offset);
            if (view.IsEnd)
            {
                return null;
            }

            if (view.Keyword.SequenceEqual("EXTNAME"u8))
            {
                return view.TextValue();
            }
        }

        return null;
    }

    private static bool IsCompressedImage(ReadOnlySpan<byte> header)
    {
        for (int offset = 0; offset + FitsBlock.CardSize <= header.Length; offset += FitsBlock.CardSize)
        {
            FitsCardView view = FitsCardView.Parse(header.Slice(offset, FitsBlock.CardSize), offset);
            if (view.IsEnd)
            {
                return false;
            }

            if (view.Keyword.SequenceEqual("ZIMAGE"u8) && view.Kind == FitsValueKind.Logical && view.Logical)
            {
                return true;
            }
        }

        return false;
    }

    private static void ReadExact(Stream stream, Span<byte> destination)
    {
        int read = 0;
        while (read < destination.Length)
        {
            int count = stream.Read(destination[read..]);
            if (count == 0)
            {
                throw new FitsException(
                    "The file ends before the data array is complete.",
                    new FitsLocation(Position: stream.CanSeek ? stream.Position : 0));
            }

            read += count;
        }
    }
}
