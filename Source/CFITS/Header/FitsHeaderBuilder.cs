namespace CFITS;

public sealed class FitsHeaderBuilder
{
    private readonly List<CardRecord> _records = [];
    private readonly List<CardRecord> _reserved = [];

    public FitsHeaderBuilder Comment(string text) => Add(new FitsCard("COMMENT", FitsValueKind.Commentary, false, 0, 0, 0, text, "", null));

    public FitsHeaderBuilder History(string text) => Add(new FitsCard("HISTORY", FitsValueKind.Commentary, false, 0, 0, 0, text, "", null));

    public FitsHeaderBuilder Blank(string text) => Add(new FitsCard("", FitsValueKind.Commentary, false, 0, 0, 0, text, "", null));

    /// <summary>Reserves fixed physical card slots that can be filled later.</summary>
    public FitsHeaderBuilder Reserve(int count)
    {
        if (count < 0)
        {
            throw new FitsException("Reserved header space cannot have a negative card count.", new FitsLocation());
        }

        for (int i = 0; i < count; i++)
        {
            var record = new CardRecord
            {
                Card = new FitsCard("", FitsValueKind.Commentary, false, 0, 0, 0, "", "", null),
            };
            _records.Add(record);
            _reserved.Add(record);
        }

        return this;
    }

    public FitsHeaderBuilder ReplaceText(int slot, string keyword, string value, string? comment = null) =>
        Replace(slot, new FitsCard(Normalize(keyword), FitsValueKind.String, false, 0, 0, 0, value, comment ?? "", null));

    public FitsHeaderBuilder ReplaceInteger(int slot, string keyword, long value, string? comment = null) =>
        Replace(slot, new FitsCard(Normalize(keyword), FitsValueKind.Integer, false, value, value, 0, null, comment ?? "", null));

    public FitsHeaderBuilder ReplaceReal(int slot, string keyword, double value, string? comment = null) =>
        Replace(slot, new FitsCard(Normalize(keyword), FitsValueKind.Real, false, 0, value, 0, null, comment ?? "", UnitOf(comment)));

    public FitsHeaderBuilder ReplaceLogical(int slot, string keyword, bool value, string? comment = null) =>
        Replace(slot, new FitsCard(Normalize(keyword), FitsValueKind.Logical, value, 0, 0, 0, null, comment ?? "", null));

    public FitsHeaderBuilder Replace(int slot, FitsCard card)
    {
        if ((uint)slot >= (uint)_reserved.Count)
        {
            throw new FitsException($"Reserved header slot {slot} does not exist.", new FitsLocation());
        }

        _reserved[slot].Card = card;
        return this;
    }

    public FitsHeaderBuilder Insert(int slot, FitsCard card) => Replace(slot, card);

    public FitsHeaderBuilder InsertText(int slot, string keyword, string value, string? comment = null) =>
        ReplaceText(slot, keyword, value, comment);

    public FitsHeaderBuilder InsertInteger(int slot, string keyword, long value, string? comment = null) =>
        ReplaceInteger(slot, keyword, value, comment);

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

    internal void Remove(string keyword)
    {
        string normalized = Normalize(keyword);
        _records.RemoveAll(record => record.Card.Keyword.Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

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
