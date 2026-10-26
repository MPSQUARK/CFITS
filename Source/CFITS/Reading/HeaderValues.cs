namespace CFITS;

/// <summary>Reads a few header keywords from raw card bytes, without building a header object.</summary>
internal static class HeaderValues
{
    public static bool TryLast(ReadOnlySpan<byte> header, ReadOnlySpan<byte> keyword, out FitsCardView view)
    {
        view = default;
        bool found = false;
        for (int offset = 0; offset + FitsBlock.CardSize <= header.Length; offset += FitsBlock.CardSize)
        {
            FitsCardView card = FitsCardView.Parse(header.Slice(offset, FitsBlock.CardSize), offset);
            if (card.IsEnd)
            {
                break;
            }

            if (card.Keyword.SequenceEqual(keyword))
            {
                view = card;
                found = true;
            }
        }

        return found;
    }

    public static double Real(ReadOnlySpan<byte> header, ReadOnlySpan<byte> keyword, double fallback)
    {
        if (!TryLast(header, keyword, out FitsCardView view))
        {
            return fallback;
        }

        return view.Kind switch
        {
            FitsValueKind.Real => view.Real,
            FitsValueKind.Integer => view.Integer,
            _ => fallback,
        };
    }

    public static long? Integer(ReadOnlySpan<byte> header, ReadOnlySpan<byte> keyword)
    {
        if (!TryLast(header, keyword, out FitsCardView view) || view.Kind != FitsValueKind.Integer)
        {
            return null;
        }

        return view.Integer;
    }
}
