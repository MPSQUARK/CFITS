namespace CFITS;

internal readonly struct HduShape
{
    public int Bitpix { get; init; }

    public int[] Axes { get; init; }

    public long Pcount { get; init; }

    public long Gcount { get; init; }

    public bool Groups { get; init; }

    public string? Extension { get; init; }

    public long DataBytes { get; init; }

    public bool Primary { get; init; }
}

internal static class DataLayout
{
    private static readonly int[] LegalBitpix = [8, 16, 32, 64, -32, -64];

    public static HduShape Measure(ReadOnlySpan<byte> header, int hduIndex)
    {
        int? bitpix = null;
        int? naxis = null;
        var axes = new Dictionary<int, int>();
        long? pcount = null;
        long? gcount = null;
        bool groups = false;
        string? extension = null;
        bool simple = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        int offset = 0;
        while (offset + FitsBlock.CardSize <= header.Length)
        {
            var view = FitsCardView.Parse(header.Slice(offset, FitsBlock.CardSize), offset);
            offset += FitsBlock.CardSize;
            if (view.IsEnd)
            {
                break;
            }

            string keyword = view.KeywordText().ToUpperInvariant();
            if (keyword is "COMMENT" or "HISTORY" or "")
            {
                continue;
            }

            if (keyword is "SIMPLE" or "XTENSION" or "BITPIX" or "NAXIS" or "PCOUNT" or "GCOUNT" or "GROUPS")
            {
                if (!seen.Add(keyword))
                {
                    throw new FitsException($"Keyword {keyword} is repeated.", new FitsLocation(hduIndex, keyword));
                }
            }

            switch (keyword)
            {
                case "SIMPLE":
                    simple = view.Kind == FitsValueKind.Logical && view.Logical;
                    if (!simple)
                    {
                        throw new FitsException("SIMPLE must be the logical value T.", new FitsLocation(hduIndex, keyword));
                    }

                    break;
                case "XTENSION":
                    extension = (view.TextValue() ?? "").Trim();
                    break;
                case "BITPIX":
                    bitpix = checked((int)view.Integer);
                    break;
                case "NAXIS":
                    naxis = checked((int)view.Integer);
                    break;
                case "PCOUNT":
                    pcount = view.Integer;
                    break;
                case "GCOUNT":
                    gcount = view.Integer;
                    break;
                case "GROUPS":
                    groups = view.Kind == FitsValueKind.Logical && view.Logical;
                    break;
                default:
                    if (keyword.StartsWith("NAXIS", StringComparison.Ordinal) && keyword.Length > 5 && int.TryParse(keyword[5..], out int axis) && axis > 0)
                    {
                        axes[axis] = checked((int)view.Integer);
                    }

                    break;
            }
        }

        if (bitpix is null || naxis is null)
        {
            throw new FitsException("The header is missing BITPIX or NAXIS.", new FitsLocation(hduIndex));
        }

        if (!LegalBitpix.Contains(bitpix.Value))
        {
            throw new FitsException($"BITPIX {bitpix.Value} is not a legal FITS value.", new FitsLocation(hduIndex, "BITPIX"));
        }

        if (naxis is < 0 or > 999)
        {
            throw new FitsException("NAXIS must be from 0 through 999.", new FitsLocation(hduIndex, "NAXIS"));
        }

        var ordered = new int[naxis.Value];
        for (int axis = 1; axis <= naxis.Value; axis++)
        {
            if (!axes.TryGetValue(axis, out int length))
            {
                throw new FitsException($"NAXIS{axis} is missing.", new FitsLocation(hduIndex, $"NAXIS{axis}"));
            }

            ordered[axis - 1] = length;
        }

        bool primary = extension is null;
        if (primary && !simple && hduIndex == 0)
        {
            throw new FitsException("The primary header is missing SIMPLE.", new FitsLocation(hduIndex, "SIMPLE"));
        }

        if (!primary && (pcount is null || gcount is null))
        {
            throw new FitsException("An extension header requires PCOUNT and GCOUNT.", new FitsLocation(hduIndex));
        }

        long parameters = pcount ?? 0;
        long groupsCount = gcount ?? 1;
        long elements = ElementProduct(ordered, groups ? 1 : 0);
        long bytesPer = Math.Abs(bitpix.Value) / 8;
        long dataBytes = checked(bytesPer * groupsCount * (parameters + elements));
        return new HduShape
        {
            Bitpix = bitpix.Value,
            Axes = ordered,
            Pcount = parameters,
            Gcount = groupsCount,
            Groups = groups,
            Extension = extension,
            DataBytes = dataBytes,
            Primary = primary,
        };
    }

    private static long ElementProduct(int[] axes, int start)
    {
        if (axes.Length <= start)
        {
            return 0;
        }

        long product = 1;
        for (int i = start; i < axes.Length; i++)
        {
            product = checked(product * axes[i]);
        }

        return product;
    }
}
