using System.Text;

namespace CFITS.Header;

internal static class CardRecordExtensions
{
    public static void AppendView(this CardRecord target, CardRecord fragment)
    {
        target.Images.AddRange(fragment.Images);
        target.AppendText(fragment.Card.Text);
        target.AppendComment(fragment.Card.Comment);
        target.Continues = fragment.Continues;
    }

    public static void Complete(this CardRecord target)
    {
        if (target.TextBuilder is null && target.CommentBuilder is null)
        {
            return;
        }

        target.Card = target.Card with
        {
            Text = target.TextBuilder?.ToString() ?? target.Card.Text,
            Comment = target.CommentBuilder?.ToString() ?? target.Card.Comment,
        };
        target.TextBuilder = null;
        target.CommentBuilder = null;
    }

    private static void AppendText(this CardRecord target, string? fragment)
    {
        target.TextBuilder ??= new StringBuilder(target.Card.Text);
        target.TextBuilder.Append(fragment);
    }

    private static void AppendComment(this CardRecord target, string? fragment)
    {
        if (string.IsNullOrEmpty(fragment))
        {
            return;
        }

        if (string.IsNullOrEmpty(target.Card.Comment))
        {
            target.CommentBuilder ??= new StringBuilder();
            target.CommentBuilder.Append(fragment);
            return;
        }

        target.CommentBuilder ??= new StringBuilder(target.Card.Comment);
        target.CommentBuilder.Append(' ').Append(fragment);
    }
}
