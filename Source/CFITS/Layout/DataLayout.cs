namespace CFITS.Layout;

internal static class DataLayout
{
    public static HduShape Measure(ReadOnlySpan<byte> header, int hduIndex, int headerOffset)
    {
        int? bitpix = null;
        int? naxis = null;
        var axes = new int[1000];
        var axisSeen = new bool[1000];
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
        bool seenBscale = false;
        bool seenBzero = false;
        bool seenBlank = false;
        bool seenExtend = false;
        long pcountPosition = headerOffset;
        long gcountPosition = headerOffset;
        int phase = 0;
        int nextAxis = 1;
        long? blank = null;
        double? bscale = null;
        bool hasPcount = false;
        bool hasGcount = false;
        string? randomGroupsEarlyKeyword = null;
        long randomGroupsEarlyKeywordPosition = headerOffset;
        int offset = 0;
        int axisCount = 0;
        while (offset + FitsBlock.CardSize <= header.Length)
        {
            int cardOffset = offset;
            var view = FitsCardView.Parse(header.Slice(offset, FitsBlock.CardSize), headerOffset + offset);
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
            if (view.IsContinue)
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
                HeaderKeyword.Bscale => seenBscale,
                HeaderKeyword.Bzero => seenBzero,
                HeaderKeyword.Blank => seenBlank,
                HeaderKeyword.Extend => seenExtend,
                HeaderKeyword.Axis => axis < axes.Length && axisSeen[axis],
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
                case HeaderKeyword.Bscale: seenBscale = true; break;
                case HeaderKeyword.Bzero: seenBzero = true; break;
                case HeaderKeyword.Blank: seenBlank = true; break;
                case HeaderKeyword.Extend: seenExtend = true; break;
            }

            if (duplicate)
            {
                string duplicateKeyword = view.KeywordText().ToUpperInvariant();
                throw new FitsException($"Keyword {duplicateKeyword} is repeated.", At(hduIndex, duplicateKeyword, headerOffset + cardOffset));
            }

            if (kind == HeaderKeyword.Other && phase <= 3)
            {
                string keywordText = view.KeywordText();
                randomGroupsEarlyKeyword ??= keywordText;
                if (randomGroupsEarlyKeyword == keywordText)
                {
                    randomGroupsEarlyKeywordPosition = headerOffset + cardOffset;
                }
            }

            switch (kind)
            {
                case HeaderKeyword.Simple:
                    RequireOrder(phase, 0, view, hduIndex, headerOffset + cardOffset);
                    phase = 0;
                    RequireKind(view, FitsValueKind.Logical, "SIMPLE", hduIndex, headerOffset + cardOffset);
                    simple = view.Kind == FitsValueKind.Logical && view.Logical;
                    if (!simple)
                    {
                        throw new FitsException("SIMPLE must be the logical value T.", At(hduIndex, "SIMPLE", headerOffset + cardOffset));
                    }
                    break;
                case HeaderKeyword.Extension:
                    RequireOrder(phase, 0, view, hduIndex, headerOffset + cardOffset);
                    phase = 0;
                    RequireKind(view, FitsValueKind.String, "XTENSION", hduIndex, headerOffset + cardOffset);
                    extension = (view.TextValue() ?? "").Trim().ToUpperInvariant();
                    break;
                case HeaderKeyword.Bitpix:
                    RequireOrder(phase, 1, view, hduIndex, headerOffset + cardOffset);
                    phase = 1;
                    RequireKind(view, FitsValueKind.Integer, "BITPIX", hduIndex, headerOffset + cardOffset);
                    bitpix = ToInt32(view.Integer, "BITPIX", hduIndex, headerOffset + cardOffset);
                    break;
                case HeaderKeyword.Naxis:
                    RequireOrder(phase, 2, view, hduIndex, headerOffset + cardOffset);
                    RequireKind(view, FitsValueKind.Integer, "NAXIS", hduIndex, headerOffset + cardOffset);
                    naxis = ToInt32(view.Integer, "NAXIS", hduIndex, headerOffset + cardOffset);
                    phase = naxis == 0 ? 4 : 2;
                    break;
                case HeaderKeyword.Pcount:
                    RequireOrder(phase, 5, view, hduIndex, headerOffset + cardOffset);
                    phase = 5;
                    RequireKind(view, FitsValueKind.Integer, "PCOUNT", hduIndex, headerOffset + cardOffset);
                    pcount = view.Integer;
                    pcountPosition = headerOffset + cardOffset;
                    hasPcount = true;
                    break;
                case HeaderKeyword.Gcount:
                    RequireOrder(phase, 6, view, hduIndex, headerOffset + cardOffset);
                    phase = 6;
                    RequireKind(view, FitsValueKind.Integer, "GCOUNT", hduIndex, headerOffset + cardOffset);
                    gcount = view.Integer;
                    gcountPosition = headerOffset + cardOffset;
                    hasGcount = true;
                    break;
                case HeaderKeyword.Groups:
                    RequireOrder(phase, 4, view, hduIndex, headerOffset + cardOffset);
                    phase = 4;
                    RequireKind(view, FitsValueKind.Logical, "GROUPS", hduIndex, headerOffset + cardOffset);
                    groups = view.Kind == FitsValueKind.Logical && view.Logical;
                    break;
                case HeaderKeyword.Axis:
                    RequireOrder(phase, 3, view, hduIndex, headerOffset + cardOffset);
                    if ((uint)axis >= (uint)axes.Length)
                    {
                        throw new FitsException(
                            $"NAXIS{axis} is outside the supported axis range.",
                            At(hduIndex, view.KeywordText(), headerOffset + cardOffset));
                    }

                    if (axis != nextAxis)
                    {
                        throw new FitsException($"NAXIS{axis} is out of order.", At(hduIndex, $"NAXIS{axis}", headerOffset + cardOffset));
                    }

                    RequireKind(view, FitsValueKind.Integer, $"NAXIS{axis}", hduIndex, headerOffset + cardOffset);
                    nextAxis++;
                    axes[axis] = ToInt32(view.Integer, $"NAXIS{axis}", hduIndex, headerOffset + cardOffset);
                    axisSeen[axis] = true;
                    axisCount++;
                    break;
                case HeaderKeyword.Bscale:
                    RequireKind(view, FitsValueKind.Integer, "BSCALE", hduIndex, headerOffset + cardOffset, FitsValueKind.Real);
                    bscale = view.Kind == FitsValueKind.Integer ? view.Integer : view.Real;
                    break;
                case HeaderKeyword.Bzero:
                    RequireKind(view, FitsValueKind.Integer, "BZERO", hduIndex, headerOffset + cardOffset, FitsValueKind.Real);
                    break;
                case HeaderKeyword.Blank:
                    RequireKind(view, FitsValueKind.Integer, "BLANK", hduIndex, headerOffset + cardOffset);
                    blank = view.Integer;
                    break;
                case HeaderKeyword.Extend:
                    if (phase < 2)
                    {
                        throw new FitsException("Keyword EXTEND must follow NAXIS.", At(hduIndex, "EXTEND", headerOffset + cardOffset));
                    }
                    RequireKind(view, FitsValueKind.Logical, "EXTEND", hduIndex, headerOffset + cardOffset);
                    break;
            }
        }

        if (bitpix is null || naxis is null)
        {
            string missing = bitpix is null ? "BITPIX" : "NAXIS";
            throw new FitsException($"The header is missing {missing}.", At(hduIndex, missing, headerOffset + offset));
        }

        if (extension is not null && extension.Length == 0)
        {
            throw new FitsException("XTENSION must not be empty.", At(hduIndex, "XTENSION", headerOffset));
        }

        if (extension is not null && seenSimple)
        {
            throw new FitsException("SIMPLE is not allowed in an extension HDU.", At(hduIndex, "SIMPLE", headerOffset + offset));
        }

        if (hduIndex == 0 && extension is null && !seenSimple)
        {
            throw new FitsException("The primary header is missing SIMPLE.", At(hduIndex, "SIMPLE", headerOffset + offset));
        }

        if (bitpix.Value is not (8 or 16 or 32 or 64 or -32 or -64))
        {
            throw new FitsException($"BITPIX {bitpix.Value} is not a legal FITS value.", At(hduIndex, "BITPIX", headerOffset + offset));
        }

        if (naxis is < 0 or > 999)
        {
            throw new FitsException("NAXIS must be from 0 through 999.", At(hduIndex, "NAXIS", headerOffset + offset));
        }

        var ordered = new int[naxis.Value];
        for (int axis = 1; axis <= naxis.Value; axis++)
        {
            if (!axisSeen[axis])
            {
                throw new FitsException($"NAXIS{axis} is missing.", At(hduIndex, $"NAXIS{axis}", headerOffset + offset));
            }

            int length = axes[axis];
            if (length < 0)
            {
                throw new FitsException($"NAXIS{axis} must not be negative.", At(hduIndex, $"NAXIS{axis}", headerOffset + offset));
            }

            ordered[axis - 1] = length;
        }

        if (naxis.Value == 0 && axisCount > 0)
        {
            throw new FitsException("NAXIS1 and other axis keywords are not allowed when NAXIS is zero.", At(hduIndex, "NAXIS1", headerOffset + offset));
        }
        if (axisCount != naxis.Value)
        {
            int unexpectedAxis = 0;
            for (int axis = naxis.Value + 1; axis < axisSeen.Length; axis++)
            {
                if (axisSeen[axis])
                {
                    unexpectedAxis = axis;
                    break;
                }
            }
            throw new FitsException(
                unexpectedAxis > 0
                    ? $"NAXIS{unexpectedAxis} is not allowed when NAXIS is {naxis.Value}."
                    : "The header contains an invalid number of NAXISn keywords.",
                At(hduIndex, unexpectedAxis > 0 ? $"NAXIS{unexpectedAxis}" : "NAXIS", headerOffset + offset));
        }

        bool primary = extension is null;
        if (!primary && seenExtend)
        {
            throw new FitsException("EXTEND is only valid in the primary HDU.", At(hduIndex, "EXTEND", headerOffset + offset));
        }
        if (primary && !simple && hduIndex == 0)
        {
            throw new FitsException("The primary header is missing SIMPLE.", At(hduIndex, "SIMPLE", headerOffset + offset));
        }
        if (seenGroups && !groups)
        {
            throw new FitsException("GROUPS must be the logical value T when present.", At(hduIndex, "GROUPS", headerOffset + offset));
        }

        if (!primary && (pcount is null || gcount is null))
        {
            throw new FitsException("An extension header requires PCOUNT and GCOUNT.", At(hduIndex, pcount is null ? "PCOUNT" : "GCOUNT", headerOffset + offset));
        }

        if (extension is "IMAGE")
        {
            if (pcount is not 0)
            {
                throw new FitsException(
                    "IMAGE extensions require PCOUNT = 0.",
                    At(hduIndex, "PCOUNT", pcount is null ? headerOffset + offset : pcountPosition));
            }

            if (gcount is not 1)
            {
                throw new FitsException(
                    "IMAGE extensions require GCOUNT = 1.",
                    At(hduIndex, "GCOUNT", gcount is null ? headerOffset + offset : gcountPosition));
            }
        }

        if (primary && !groups && (hasPcount || hasGcount))
        {
            throw new FitsException("PCOUNT and GCOUNT require GROUPS=T in the primary HDU.", At(hduIndex, "GROUPS", headerOffset + offset));
        }

        if (groups)
        {
            if (randomGroupsEarlyKeyword is not null)
            {
                throw new FitsException(
                    $"Keyword {randomGroupsEarlyKeyword} cannot appear before the last NAXISn keyword in a random-groups header.",
                    At(hduIndex, randomGroupsEarlyKeyword, randomGroupsEarlyKeywordPosition));
            }

            if (!primary || naxis.Value < 2 || ordered[0] != 0)
            {
                throw new FitsException("Random groups require a primary HDU with NAXIS1=0.", At(hduIndex, "NAXIS1", headerOffset + offset));
            }
            for (int axis = 1; axis < ordered.Length; axis++)
            {
                if (ordered[axis] <= 0)
                {
                    throw new FitsException(
                        $"Random groups require NAXIS{axis + 1} to be positive.",
                        At(hduIndex, $"NAXIS{axis + 1}", headerOffset + offset));
                }
            }
            if (!hasPcount || !hasGcount)
            {
                throw new FitsException(
                    "Random groups require PCOUNT and GCOUNT.",
                    At(hduIndex, hasPcount ? "GCOUNT" : "PCOUNT", headerOffset + offset));
            }
        }

        if (bscale is 0)
        {
            throw new FitsException("BSCALE must not be zero.", At(hduIndex, "BSCALE", headerOffset + offset));
        }

        if (blank is not null)
        {
            if (bitpix.Value < 0)
            {
                throw new FitsException("BLANK is only valid for integer images.", At(hduIndex, "BLANK", headerOffset + offset));
            }

            long minimum = bitpix.Value switch
            {
                8 => 0,
                64 => long.MinValue,
                _ => -(1L << (bitpix.Value - 1)),
            };
            long maximum = bitpix.Value switch
            {
                8 => byte.MaxValue,
                64 => long.MaxValue,
                _ => (1L << (bitpix.Value - 1)) - 1,
            };
            if (blank.Value < minimum || blank.Value > maximum)
            {
                throw new FitsException("BLANK is outside the stored integer domain.", At(hduIndex, "BLANK", headerOffset + offset));
            }
        }

        long parameters = pcount ?? 0;
        long groupsCount = gcount ?? 1;
        if (parameters < 0)
        {
            throw new FitsException("PCOUNT must not be negative.", At(hduIndex, "PCOUNT", headerOffset + offset));
        }
        if (parameters > int.MaxValue)
        {
            throw new FitsException("PCOUNT exceeds the supported random-group parameter count.", At(hduIndex, "PCOUNT", headerOffset + offset));
        }

        if (groups ? groupsCount < 0 : groupsCount != 1)
        {
            string requirement = groups ? "non-negative" : "equal to one";
            throw new FitsException($"GCOUNT must be {requirement}.", At(hduIndex, "GCOUNT", headerOffset + offset));
        }
        if (groupsCount > int.MaxValue)
        {
            throw new FitsException("GCOUNT exceeds the supported random-group count.", At(hduIndex, "GCOUNT", headerOffset + offset));
        }

        long dataBytes;
        try
        {
            long elements = ElementProduct(ordered, groups ? 1 : 0);
            long bytesPer = Math.Abs(bitpix.Value) / 8;
            dataBytes = checked(bytesPer * groupsCount * (parameters + elements));
        }
        catch (OverflowException error)
        {
            throw new FitsException(
                "The declared image dimensions exceed the supported data size.",
                At(hduIndex, "NAXIS", headerOffset + offset),
                error);
        }
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

    private static void RequireOrder(int phase, int required, FitsCardView view, int hduIndex, long position)
    {
        if (phase > required)
        {
            throw new FitsException($"Keyword {view.KeywordText()} is out of order.", At(hduIndex, view.KeywordText(), position));
        }
    }

    private static void RequireKind(
        FitsCardView view,
        FitsValueKind expected,
        string keyword,
        int hduIndex,
        long position,
        FitsValueKind? alternate = null)
    {
        if (view.Kind != expected && view.Kind != alternate)
        {
            throw new FitsException($"Keyword {keyword} has an undefined or invalid value.", At(hduIndex, keyword, position));
        }
    }

    private static FitsLocation At(int hduIndex, string? keyword, long position) =>
        new(hduIndex, keyword, Position: position);

    private static int ToInt32(long value, string keyword, int hduIndex, long position)
    {
        if (value is < int.MinValue or > int.MaxValue)
        {
            throw new FitsException($"Keyword {keyword} is outside the 32-bit FITS integer range.", At(hduIndex, keyword, position));
        }

        return (int)value;
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
        Bscale,
        Bzero,
        Blank,
        Extend,
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
        if (EqualsKeyword(keyword, "BSCALE"u8)) return HeaderKeyword.Bscale;
        if (EqualsKeyword(keyword, "BZERO"u8)) return HeaderKeyword.Bzero;
        if (EqualsKeyword(keyword, "BLANK"u8)) return HeaderKeyword.Blank;
        if (EqualsKeyword(keyword, "EXTEND"u8)) return HeaderKeyword.Extend;
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
