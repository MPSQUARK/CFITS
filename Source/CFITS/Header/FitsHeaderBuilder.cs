namespace CFITS;

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
