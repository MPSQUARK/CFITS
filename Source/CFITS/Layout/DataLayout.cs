namespace CFITS;

internal static class DataLayout
{
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
        bool seenSimple = false;
        bool seenExtension = false;
        bool seenBitpix = false;
        bool seenNaxis = false;
        bool seenPcount = false;
        bool seenGcount = false;
        bool seenGroups = false;

        int offset = 0;
        while (offset + FitsBlock.CardSize <= header.Length)
        {
            var view = FitsCardView.Parse(header.Slice(offset, FitsBlock.CardSize), offset);
            offset += FitsBlock.CardSize;
            if (view.IsEnd)
            {
                break;
            }

            ReadOnlySpan<byte> keyword = view.Keyword;
            if (keyword.IsEmpty || EqualsKeyword(keyword, "COMMENT"u8) || EqualsKeyword(keyword, "HISTORY"u8))
            {
                continue;
            }

            HeaderKeyword kind = ClassifyKeyword(keyword, out int axis);
            bool duplicate = kind switch
            {
                HeaderKeyword.Simple => seenSimple,
                HeaderKeyword.Extension => seenExtension,
                HeaderKeyword.Bitpix => seenBitpix,
                HeaderKeyword.Naxis => seenNaxis,
                HeaderKeyword.Pcount => seenPcount,
                HeaderKeyword.Gcount => seenGcount,
                HeaderKeyword.Groups => seenGroups,
                _ => false,
            };
            switch (kind)
            {
                case HeaderKeyword.Simple: seenSimple = true; break;
                case HeaderKeyword.Extension: seenExtension = true; break;
                case HeaderKeyword.Bitpix: seenBitpix = true; break;
                case HeaderKeyword.Naxis: seenNaxis = true; break;
                case HeaderKeyword.Pcount: seenPcount = true; break;
                case HeaderKeyword.Gcount: seenGcount = true; break;
                case HeaderKeyword.Groups: seenGroups = true; break;
            }

            if (duplicate)
            {
                string duplicateKeyword = view.KeywordText().ToUpperInvariant();
                throw new FitsException($"Keyword {duplicateKeyword} is repeated.", new FitsLocation(hduIndex, duplicateKeyword));
            }

            switch (kind)
            {
                case HeaderKeyword.Simple:
                    simple = view.Kind == FitsValueKind.Logical && view.Logical;
                    if (!simple)
                    {
                        throw new FitsException("SIMPLE must be the logical value T.", new FitsLocation(hduIndex, "SIMPLE"));
                    }
                    break;
                case HeaderKeyword.Extension:
                    extension = (view.TextValue() ?? "").Trim();
                    break;
                case HeaderKeyword.Bitpix:
                    bitpix = checked((int)view.Integer);
                    break;
                case HeaderKeyword.Naxis:
                    naxis = checked((int)view.Integer);
                    break;
                case HeaderKeyword.Pcount:
                    pcount = view.Integer;
                    break;
                case HeaderKeyword.Gcount:
                    gcount = view.Integer;
                    break;
                case HeaderKeyword.Groups:
                    groups = view.Kind == FitsValueKind.Logical && view.Logical;
                    break;
                case HeaderKeyword.Axis:
                    axes[axis] = checked((int)view.Integer);
                    break;
            }
        }

        if (bitpix is null || naxis is null)
        {
            throw new FitsException("The header is missing BITPIX or NAXIS.", new FitsLocation(hduIndex));
        }

        if (bitpix.Value is not (8 or 16 or 32 or 64 or -32 or -64))
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

            if (length < 0)
            {
                throw new FitsException($"NAXIS{axis} must not be negative.", new FitsLocation(hduIndex, $"NAXIS{axis}"));
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
        if (parameters < 0)
        {
            throw new FitsException("PCOUNT must not be negative.", new FitsLocation(hduIndex, "PCOUNT"));
        }

        if (groupsCount <= 0)
        {
            throw new FitsException("GCOUNT must be positive.", new FitsLocation(hduIndex, "GCOUNT"));
        }

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

    private static bool EqualsKeyword(ReadOnlySpan<byte> keyword, ReadOnlySpan<byte> expected)
    {
        if (keyword.Length != expected.Length)
        {
            return false;
        }

        for (int i = 0; i < keyword.Length; i++)
        {
            byte actual = keyword[i];
            if (actual is >= (byte)'a' and <= (byte)'z')
            {
                actual = (byte)(actual - ('a' - 'A'));
            }

            if (actual != expected[i])
            {
                return false;
            }
        }

        return true;
    }

    private enum HeaderKeyword
    {
        Other,
        Simple,
        Extension,
        Bitpix,
        Naxis,
        Pcount,
        Gcount,
        Groups,
        Axis,
    }

    private static HeaderKeyword ClassifyKeyword(ReadOnlySpan<byte> keyword, out int axis)
    {
        axis = 0;
        if (EqualsKeyword(keyword, "SIMPLE"u8)) return HeaderKeyword.Simple;
        if (EqualsKeyword(keyword, "XTENSION"u8)) return HeaderKeyword.Extension;
        if (EqualsKeyword(keyword, "BITPIX"u8)) return HeaderKeyword.Bitpix;
        if (EqualsKeyword(keyword, "NAXIS"u8)) return HeaderKeyword.Naxis;
        if (EqualsKeyword(keyword, "PCOUNT"u8)) return HeaderKeyword.Pcount;
        if (EqualsKeyword(keyword, "GCOUNT"u8)) return HeaderKeyword.Gcount;
        if (EqualsKeyword(keyword, "GROUPS"u8)) return HeaderKeyword.Groups;
        if (keyword.Length <= 5 || !EqualsKeyword(keyword[..5], "NAXIS"u8))
        {
            return HeaderKeyword.Other;
        }

        int value = 0;
        for (int i = 5; i < keyword.Length; i++)
        {
            byte digit = keyword[i];
            if (digit is < (byte)'0' or > (byte)'9')
            {
                return HeaderKeyword.Other;
            }

            value = checked(value * 10 + digit - (byte)'0');
        }

        if (value <= 0)
        {
            return HeaderKeyword.Other;
        }

        axis = value;
        return HeaderKeyword.Axis;
    }
}
