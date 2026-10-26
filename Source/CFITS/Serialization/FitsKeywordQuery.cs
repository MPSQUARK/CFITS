namespace CFITS;

/// <summary>
/// One keyword the generated model reads. Scanning fills the value fields and ignores every other card.
/// </summary>
public record struct FitsKeywordQuery(string Name, FitsKeywordKind Kind)
{
    public bool Found { get; set; }

    public bool Logical { get; set; }

    public long Integer { get; set; }

    public double Real { get; set; }

    public string? Text { get; set; }

    internal FitsValueKind StoredKind { get; set; }

    public static void Bind(ReadOnlySpan<byte> header, Span<FitsKeywordQuery> queries)
    {
        if (queries.IsEmpty)
        {
            return;
        }

        Span<byte> encoded = stackalloc byte[8];
        for (int offset = 0; offset + FitsBlock.CardSize <= header.Length; offset += FitsBlock.CardSize)
        {
            FitsCardView card = FitsCardView.Parse(header.Slice(offset, FitsBlock.CardSize), offset);
            if (card.IsEnd)
            {
                break;
            }

            ReadOnlySpan<byte> keyword = card.Keyword;
            for (int index = 0; index < queries.Length; index++)
            {
                if (!Matches(keyword, queries[index].Name, encoded))
                {
                    continue;
                }

                queries[index] = queries[index] with
                {
                    Found = true,
                    StoredKind = card.Kind,
                    Logical = card.Logical,
                    Integer = card.Integer,
                    Real = card.Kind == FitsValueKind.Integer ? card.Integer : card.Real,
                    Text = queries[index].Kind == FitsKeywordKind.Text ? card.TextValue() : null,
                };
            }
        }

        for (int index = 0; index < queries.Length; index++)
        {
            FitsKeywordQuery query = queries[index];
            if (!query.Found)
            {
                throw new FitsException($"Keyword '{query.Name}' was not found.", new FitsLocation(Keyword: query.Name));
            }

            bool valid = query.Kind switch
            {
                FitsKeywordKind.Logical => query.StoredKind == FitsValueKind.Logical,
                FitsKeywordKind.Integer => query.StoredKind == FitsValueKind.Integer,
                FitsKeywordKind.Real => query.StoredKind is FitsValueKind.Integer or FitsValueKind.Real,
                _ => query.StoredKind is FitsValueKind.String or FitsValueKind.Commentary or FitsValueKind.Raw or FitsValueKind.Undefined,
            };
            if (!valid)
            {
                string expected = query.Kind switch
                {
                    FitsKeywordKind.Logical => "a logical value",
                    FitsKeywordKind.Integer => "an integer value",
                    FitsKeywordKind.Real => "a numeric value",
                    _ => "a string value",
                };
                throw new FitsException($"Keyword '{query.Name}' is not {expected}.", new FitsLocation(Keyword: query.Name));
            }
        }
    }

    private static bool Matches(ReadOnlySpan<byte> keyword, string name, Span<byte> encoded)
    {
        if (name.Length is < 1 or > 8 || keyword.Length != name.Length)
        {
            return false;
        }

        for (int index = 0; index < name.Length; index++)
        {
            char character = name[index];
            if (character > 127)
            {
                return false;
            }

            encoded[index] = (byte)character;
        }

        return keyword.SequenceEqual(encoded[..name.Length]);
    }
}
