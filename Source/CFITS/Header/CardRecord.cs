using System.Text;

namespace CFITS.Header;

internal sealed class CardRecord
{
    public required FitsCard Card { get; set; }

    public List<ReadOnlyMemory<byte>> Images { get; } = [];

    public static CardRecord FromView(FitsCardView view) => FromImage(view.Image.ToArray(), view);

    public static CardRecord FromImage(ReadOnlyMemory<byte> image, FitsCardView view)
    {
        // ValidateKeyword has already restricted standard keywords to FITS uppercase ASCII.
        string keyword = view.KeywordText();
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
        record.Images.Add(image);
        return record;
    }

    public bool Continues { get; set; }

    internal StringBuilder? TextBuilder { get; set; }

    internal StringBuilder? CommentBuilder { get; set; }

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
