namespace CFITS.Header;

internal sealed class CardRecord
{
    public required FitsCard Card { get; set; }

    public List<byte[]> Images { get; } = [];

    public static CardRecord FromView(FitsCardView view)
    {
        string keyword = view.KeywordText().ToUpperInvariant();
        string? text = view.Kind switch
        {
            FitsValueKind.String => view.TextValue(),
            FitsValueKind.Commentary or FitsValueKind.Raw => view.TextValue(),
            _ => null,
        };
        bool continues = view.Kind == FitsValueKind.String && view.Continues;
        var card = new FitsCard(
            keyword,
            view.Kind,
            view.Logical,
            view.Integer,
            view.Real,
            view.Imaginary,
            text,
            view.CommentText(),
            view.UnitText());
        var record = new CardRecord { Card = card, Continues = continues };
        record.Images.Add(view.Image.ToArray());
        return record;
    }

    public bool Continues { get; set; }

    public void Append(FitsCardView view)
    {
        Images.Add(view.Image.ToArray());
        string fragment = view.TextValue() ?? "";
        Card = Card with { Text = (Card.Text ?? "") + fragment, Comment = JoinComments(Card.Comment, view.CommentText()) };
        Continues = view.Continues;
    }

    private static string JoinComments(string? left, string right)
    {
        if (string.IsNullOrEmpty(left))
        {
            return right;
        }

        if (string.IsNullOrEmpty(right))
        {
            return left;
        }

        return left + " " + right;
    }
}
