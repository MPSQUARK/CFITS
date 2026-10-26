namespace CFITS;

/// <summary>
/// Reads one FITS header from a seekable stream and positions the stream at that HDU's data.
/// </summary>
internal static class FitsHduStream
{
    internal readonly struct Opened
    {
        public byte[] Header { get; init; }

        public HduShape Shape { get; init; }

        public int Index { get; init; }
    }

    public static bool TrySelect(
        Stream stream,
        int hduIndex,
        string? extensionName,
        bool image,
        out Opened opened,
        out FitsHeader? header)
    {
        opened = default;
        header = null;
        long origin = stream.Position;
        int index = 0;
        while (true)
        {
            if (!TryReadHeader(stream, index, out byte[] bytes, out HduShape shape))
            {
                stream.Position = origin;
                return false;
            }

            bool kind = image
                ? shape.Extension is null or "IMAGE"
                : shape.Extension == "BINTABLE" && !HasHeapColumn(bytes) && !IsCompressedImage(bytes);
            bool name = extensionName is null || ExtensionName(bytes) == extensionName;
            bool indexMatches = hduIndex < 0 || index == hduIndex;
            if (kind && name && indexMatches)
            {
                opened = new Opened { Header = bytes, Shape = shape, Index = index };
                header = CreateHeader(bytes, index);
                return true;
            }

            if (hduIndex >= 0 && index == hduIndex)
            {
                stream.Position = origin;
                return false;
            }

            SkipData(stream, shape);
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
        while (TryReadHeader(stream, 1, out _, out HduShape shape))
        {
            SkipData(stream, shape);
        }
    }

    public static double Scale(FitsHeader header, string keyword) =>
        header.TryGetCard(keyword, out FitsCard card) ? card.GetDouble() : 1;

    public static double Zero(FitsHeader header, string keyword) =>
        header.TryGetCard(keyword, out FitsCard card) ? card.GetDouble() : 0;

    public static long? Null(FitsHeader header, string keyword) =>
        header.TryGetCard(keyword, out FitsCard card) && card.Kind == FitsValueKind.Integer ? card.Integer : null;

    private static bool TryReadHeader(Stream stream, int hduIndex, out byte[] header, out HduShape shape)
    {
        header = [];
        shape = default;
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

        var blocks = new List<byte[]>();
        bool ended = false;
        while (!ended)
        {
            var block = new byte[FitsBlock.Size];
            ReadExact(stream, block);
            blocks.Add(block);
            for (int offset = 0; offset < block.Length && !ended; offset += FitsBlock.CardSize)
            {
                if (block.AsSpan(offset, 8).StartsWith("END"u8) && block.AsSpan(offset, 8).TrimEnd((byte)' ').Length == 3)
                {
                    ended = true;
                }
            }
        }

        header = new byte[blocks.Count * FitsBlock.Size];
        for (int index = 0; index < blocks.Count; index++)
        {
            blocks[index].CopyTo(header, index * FitsBlock.Size);
        }

        shape = DataLayout.Measure(header, hduIndex, 0);
        return true;
    }

    private static void SkipData(Stream stream, HduShape shape) =>
        stream.Seek(FitsBlock.Pad(shape.DataBytes), SeekOrigin.Current);

    private static FitsHeader CreateHeader(byte[] header, int hduIndex)
    {
        var records = new List<CardRecord>();
        for (int offset = 0; offset + FitsBlock.CardSize <= header.Length; offset += FitsBlock.CardSize)
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

    private static bool HasHeapColumn(ReadOnlySpan<byte> header)
    {
        for (int offset = 0; offset + FitsBlock.CardSize <= header.Length; offset += FitsBlock.CardSize)
        {
            FitsCardView view = FitsCardView.Parse(header.Slice(offset, FitsBlock.CardSize), offset);
            if (view.IsEnd)
            {
                return false;
            }

            if (!view.Keyword.StartsWith("TFORM"u8))
            {
                continue;
            }

            ColumnSpec spec = ColumnFormats.Parse(view.TextValue() ?? "", ascii: false);
            if (spec.Variable)
            {
                return true;
            }
        }

        return false;
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
