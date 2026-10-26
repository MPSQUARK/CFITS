using System.Text;

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
        var cards = new FitsCard[records.Count];
        for (int index = 0; index < records.Count; index++)
        {
            cards[index] = records[index].Card;
        }

        _cards = cards;
        var lookup = new Dictionary<string, FitsCard>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            if (record.Card.Kind is not (FitsValueKind.Commentary or FitsValueKind.Raw))
            {
                lookup[record.Card.Keyword] = record.Card;
            }
        }

        _lookup = lookup;
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
        var logical = new List<CardRecord>(physical.Count);
        CardRecord? open = null;
        foreach (var record in physical)
        {
            if (open is not null && record.Card.Keyword == "CONTINUE" && record.Card.Kind == FitsValueKind.String)
            {
                open.AppendView(record);
                if (!record.Continues)
                {
                    open.Complete();
                    open = null;
                }

                continue;
            }

            if (open is not null)
            {
                // An ampersand is a continuation marker only when the next
                // physical card is a conforming CONTINUE card. Otherwise it
                // remains the literal final character of the string.
                open.Complete();
                open.Card = open.Card with { Text = (open.Card.Text ?? "") + "&" };
                open.Continues = false;
            }

            if (record.Card.Keyword == "CONTINUE")
            {
                // The convention requires readers that do not find a
                // preceding continued value to treat CONTINUE as commentary.
                record.Card = record.Card with
                {
                    Keyword = "COMMENT",
                    Kind = FitsValueKind.Commentary,
                    Text = Encoding.ASCII.GetString(record.Images[0].Span[8..]).TrimEnd(),
                    Comment = "",
                };
                logical.Add(record);
                continue;
            }

            logical.Add(record);
            open = record.Continues ? record : null;
        }

        if (open is not null)
        {
            // The final physical card is also a valid place for a literal
            // ampersand; no following CONTINUE card means no continuation.
            open.Complete();
            open.Card = open.Card with { Text = (open.Card.Text ?? "") + "&" };
            open.Continues = false;
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
                    stream.Write(image.Span);
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

