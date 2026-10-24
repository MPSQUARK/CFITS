namespace CFITS.Header;

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
