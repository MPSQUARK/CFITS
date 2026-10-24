namespace CFITS;

/// <summary>Header keywords in file order. Lookup returns the last value for a name.</summary>
public sealed class FitsHeader
{
    private readonly List<CardRecord> _records;
    private readonly IReadOnlyList<FitsCard> _cards;
    private readonly Dictionary<string, FitsCard> _lookup;

    private FitsHeader(List<CardRecord> records, int hduIndex)
    {
        _records = records;
        _cards = records.Select(static record => record.Card).ToArray();
        _lookup = new Dictionary<string, FitsCard>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            if (record.Card.Kind is not (FitsValueKind.Commentary or FitsValueKind.Raw))
            {
                _lookup[record.Card.Keyword] = record.Card;
            }
        }

        HduIndex = hduIndex;
    }

    public int HduIndex { get; }

    public IReadOnlyList<FitsCard> Cards => _cards;

    public bool TryGetCard(string keyword, out FitsCard card)
    {
        return _lookup.TryGetValue(keyword, out card);
    }

    public long GetInt64(string keyword) => Required(keyword).GetInt64();

    public double GetDouble(string keyword) => Required(keyword).GetDouble();

    public bool GetBoolean(string keyword) => Required(keyword).GetBoolean();

    public string GetString(string keyword) => Required(keyword).GetString() ?? "";

    public static FitsHeaderBuilder Builder() => new();

    internal static FitsHeader FromPhysical(IReadOnlyList<CardRecord> physical, int hduIndex)
    {
        var logical = new List<CardRecord>();
        CardRecord? open = null;
        foreach (var record in physical)
        {
            if (open is not null && record.Card.Keyword == "CONTINUE" && record.Card.Kind == FitsValueKind.String)
            {
                open.AppendView(record);
                if (!record.Continues)
                {
                    open = null;
                }

                continue;
            }

            if (open is not null)
            {
                open = null;
            }

            logical.Add(record);
            open = record.Continues ? record : null;
        }

        return new FitsHeader(logical, hduIndex);
    }

    internal void Write(Stream stream)
    {
        long written = 0;
        foreach (var record in _records)
        {
            if (record.Images.Count > 0)
            {
                foreach (var image in record.Images)
                {
                    stream.Write(image);
                    written += image.Length;
                }

                continue;
            }

            byte[] formatted = CardImages.Format(record.Card);
            stream.Write(formatted);
            written += formatted.Length;
        }

        Span<byte> end = stackalloc byte[FitsBlock.CardSize];
        end.Fill((byte)' ');
        end[0] = (byte)'E';
        end[1] = (byte)'N';
        end[2] = (byte)'D';
        stream.Write(end);
        written += FitsBlock.CardSize;
        long padded = FitsBlock.Pad(written);
        Span<byte> spaces = stackalloc byte[FitsBlock.CardSize];
        spaces.Fill((byte)' ');
        long remaining = padded - written;
        while (remaining > 0)
        {
            int count = (int)Math.Min(spaces.Length, remaining);
            stream.Write(spaces[..count]);
            remaining -= count;
        }
    }

    private FitsCard Required(string keyword)
    {
        if (!TryGetCard(keyword, out var card))
        {
            throw new FitsException($"Keyword '{keyword}' was not found.", new FitsLocation(HduIndex, keyword));
        }

        return card;
    }
}

