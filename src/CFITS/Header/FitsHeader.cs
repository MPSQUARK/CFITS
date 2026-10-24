using System.Text;

namespace CFITS;

/// <summary>Header keywords in file order. Lookup returns the last value for a name.</summary>
public sealed class FitsHeader
{
    private readonly List<CardRecord> _records;

    private FitsHeader(List<CardRecord> records, int hduIndex)
    {
        _records = records;
        HduIndex = hduIndex;
    }

    public int HduIndex { get; }

    public IReadOnlyList<FitsCard> Cards => _records.Select(static record => record.Card).ToList();

    public bool TryGetCard(string keyword, out FitsCard card)
    {
        for (int i = _records.Count - 1; i >= 0; i--)
        {
            var current = _records[i].Card;
            if (current.Kind is FitsValueKind.Commentary or FitsValueKind.Raw)
            {
                continue;
            }

            if (current.Keyword.Equals(keyword, StringComparison.OrdinalIgnoreCase))
            {
                card = current;
                return true;
            }
        }

        card = default;
        return false;
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
        foreach (var record in _records)
        {
            if (record.Images.Count > 0)
            {
                foreach (var image in record.Images)
                {
                    stream.Write(image);
                }

                continue;
            }

            stream.Write(CardImages.Format(record.Card));
        }

        Span<byte> end = stackalloc byte[FitsBlock.CardSize];
        end.Fill((byte)' ');
        end[0] = (byte)'E';
        end[1] = (byte)'N';
        end[2] = (byte)'D';
        stream.Write(end);
        long written = (_records.Sum(static record => record.Images.Count > 0 ? record.Images.Sum(static image => image.Length) : CardImages.Format(record.Card).Length) + FitsBlock.CardSize);
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

internal static class CardRecordExtensions
{
    public static void AppendView(this CardRecord target, CardRecord fragment)
    {
        target.Images.AddRange(fragment.Images);
        target.Card = target.Card with
        {
            Text = (target.Card.Text ?? "") + (fragment.Card.Text ?? ""),
            Comment = Join(target.Card.Comment, fragment.Card.Comment),
        };
        target.Continues = fragment.Continues;
    }

    private static string Join(string? left, string? right)
    {
        if (string.IsNullOrEmpty(left))
        {
            return right ?? "";
        }

        if (string.IsNullOrEmpty(right))
        {
            return left;
        }

        return left + " " + right;
    }
}

public sealed class FitsHeaderBuilder
{
    private readonly List<CardRecord> _records = [];

    public FitsHeaderBuilder Comment(string text) => Add(new FitsCard("COMMENT", FitsValueKind.Commentary, false, 0, 0, 0, text, "", null));

    public FitsHeaderBuilder History(string text) => Add(new FitsCard("HISTORY", FitsValueKind.Commentary, false, 0, 0, 0, text, "", null));

    public FitsHeaderBuilder Blank(string text) => Add(new FitsCard("", FitsValueKind.Commentary, false, 0, 0, 0, text, "", null));

    public FitsHeaderBuilder Logical(string keyword, bool value, string? comment = null) =>
        Add(new FitsCard(Normalize(keyword), FitsValueKind.Logical, value, 0, 0, 0, null, comment ?? "", null));

    public FitsHeaderBuilder Integer(string keyword, long value, string? comment = null) =>
        Add(new FitsCard(Normalize(keyword), FitsValueKind.Integer, false, value, value, 0, null, comment ?? "", null));

    public FitsHeaderBuilder Real(string keyword, double value, string? comment = null) =>
        Add(new FitsCard(Normalize(keyword), FitsValueKind.Real, false, 0, value, 0, null, comment ?? "", UnitOf(comment)));

    public FitsHeaderBuilder Text(string keyword, string value, string? comment = null) =>
        Add(new FitsCard(Normalize(keyword), FitsValueKind.String, false, 0, 0, 0, value, comment ?? "", null));

    public FitsHeaderBuilder Complex(string keyword, double real, double imaginary, string? comment = null) =>
        Add(new FitsCard(Normalize(keyword), FitsValueKind.Complex, false, 0, real, imaginary, null, comment ?? "", null));

    public FitsHeader Build() => FitsHeader.FromPhysical(_records, 0);

    private FitsHeaderBuilder Add(FitsCard card)
    {
        _records.Add(new CardRecord { Card = card });
        return this;
    }

    private static string Normalize(string keyword)
    {
        var normalized = keyword.Trim().ToUpperInvariant();
        if (normalized.Length is 0 or > 8)
        {
            throw new FitsException($"Keyword '{keyword}' must be 1 to 8 characters.", new FitsLocation(Keyword: keyword));
        }

        return normalized;
    }

    private static string? UnitOf(string? comment)
    {
        if (string.IsNullOrEmpty(comment) || comment[0] != '[')
        {
            return null;
        }

        int close = comment.IndexOf(']');
        return close > 1 ? comment[1..close] : null;
    }
}
