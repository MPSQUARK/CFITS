using System.Globalization;

namespace CFITS.Header;

internal static class CardImages
{
    public static byte[] Format(FitsCard card)
    {
        if (card.Kind == FitsValueKind.String &&
            (EncodedLength(card.Text ?? "") > 68 || !CanFitComment(card)))
        {
            return FormatLongString(card);
        }

        var image = new byte[FitsBlock.CardSize];
        FormatOne(image, card, card.Text ?? "", continueMarker: false, continued: false);
        return image;
    }

    public static void FormatOne(Span<byte> image, FitsCard card, string text, bool continueMarker, bool continued)
    {
        image.Fill((byte)' ');
        if (card.Kind == FitsValueKind.Commentary || card.Keyword.Length == 0)
        {
            WriteKeyword(image, card.Keyword);
            WriteBytes(image, 8, text);
            return;
        }

        if (continued)
        {
            WriteKeyword(image, "CONTINUE");
            WriteQuoted(image, 10, text, continueMarker);
            if (!continueMarker && !string.IsNullOrEmpty(card.Comment))
            {
                WriteComment(image, IndexOfClosingQuote(image) + 1, card.Comment);
            }

            return;
        }

        WriteKeyword(image, card.Keyword);
        image[8] = (byte)'=';
        image[9] = (byte)' ';
        switch (card.Kind)
        {
            case FitsValueKind.Logical:
                WriteFixed(image, card.Logical ? "T" : "F");
                break;
            case FitsValueKind.Integer:
                WriteFixed(image, card.Integer.ToString(CultureInfo.InvariantCulture));
                break;
            case FitsValueKind.Real:
                WriteFixed(image, FormatReal(card.Real));
                break;
            case FitsValueKind.Complex:
                WriteFree(image, FormatComplex(card));
                break;
            case FitsValueKind.String:
                WriteQuoted(image, 10, text, continueMarker);
                break;
            case FitsValueKind.Undefined:
                break;
            default:
                WriteFree(image, text);
                break;
        }

        if (!string.IsNullOrEmpty(card.Comment) && card.Kind != FitsValueKind.String)
        {
            WriteComment(image, 30, card.Comment);
        }
        else if (!string.IsNullOrEmpty(card.Comment) && card.Kind == FitsValueKind.String && !continueMarker)
        {
            int close = IndexOfClosingQuote(image);
            if (close > 0 && close + 4 < FitsBlock.CardSize)
            {
                WriteComment(image, close + 1, card.Comment);
            }
        }
    }

    private static byte[] FormatLongString(FitsCard card)
    {
        var parts = Split(card.Text ?? "", card.Comment ?? "");
        var images = new byte[parts.Count * FitsBlock.CardSize];
        for (int i = 0; i < parts.Count; i++)
        {
            bool marker = i < parts.Count - 1;
            FormatOne(images.AsSpan(i * FitsBlock.CardSize, FitsBlock.CardSize), card, parts[i], marker, continued: i > 0);
        }

        return images;
    }

    private static List<string> Split(string value, string comment)
    {
        if (comment.Length > 47)
        {
            throw new FitsException("A header comment is too long for a card.", new FitsLocation(Keyword: "CONTINUE"));
        }

        var parts = new List<string>();
        int index = 0;
        while (index < value.Length)
        {
            int remaining = EncodedLength(value[index..]);
            if (remaining <= 68 && FitsComment(value[index..], comment))
            {
                parts.Add(value[index..]);
                break;
            }

            int start = index;
            int encoded = 0;
            while (index < value.Length)
            {
                int cost = value[index] == '\'' ? 2 : 1;
                if (encoded + cost > 67)
                {
                    break;
                }

                encoded += cost;
                index++;
            }

            if (index == start)
            {
                throw new FitsException("A header string contains a quote that does not fit in one record.", new FitsLocation(Keyword: "CONTINUE"));
            }

            parts.Add(value[start..index]);
        }

        return parts;
    }

    private static int EncodedLength(string value)
    {
        int count = value.Length;
        foreach (char character in value)
        {
            if (character == '\'')
            {
                count++;
            }
        }

        return count;
    }

    private static void WriteKeyword(Span<byte> image, string keyword)
    {
        if (keyword.Length > 8)
        {
            throw new FitsException($"Keyword '{keyword}' is longer than 8 characters.", new FitsLocation(Keyword: keyword));
        }

        for (int i = 0; i < keyword.Length; i++)
        {
            char character = keyword[i];
            if (character is not ((>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_' or '-'))
            {
                throw new FitsException($"Keyword '{keyword}' contains a character the FITS standard does not allow.", new FitsLocation(Keyword: keyword));
            }

            image[i] = (byte)character;
        }
    }

    private static void WriteFixed(Span<byte> image, string text)
    {
        if (text.Length > 20)
        {
            WriteFree(image, text);
            return;
        }

        int start = 10 + 20 - text.Length;
        WriteBytes(image, start, text);
    }

    private static void WriteFree(Span<byte> image, string text) => WriteBytes(image, 10, text);

    private static void WriteQuoted(Span<byte> image, int start, string text, bool continueMarker)
    {
        ValidateAscii(text);
        if (start >= FitsBlock.CardSize)
        {
            return;
        }

        image[start] = (byte)'\'';
        int cursor = start + 1;
        foreach (char character in text)
        {
            if (cursor >= FitsBlock.CardSize - 1)
            {
                throw new FitsException("A quoted header value does not fit in one card.", new FitsLocation());
            }

            image[cursor++] = (byte)character;
            if (character == '\'' && cursor < FitsBlock.CardSize - 1)
            {
                image[cursor++] = (byte)'\'';
            }
            else if (character == '\'' && cursor >= FitsBlock.CardSize - 1)
            {
                throw new FitsException("An escaped quote does not fit in one card.", new FitsLocation());
            }
        }

        if (continueMarker)
        {
            if (cursor >= FitsBlock.CardSize - 1)
            {
                throw new FitsException("A continuation marker does not fit in one card.", new FitsLocation());
            }

            image[cursor++] = (byte)'&';
        }

        if (cursor >= FitsBlock.CardSize)
        {
            throw new FitsException("A quoted header value has no room for its closing quote.", new FitsLocation());
        }

        image[cursor] = (byte)'\'';
    }

    private static void WriteComment(Span<byte> image, int start, string comment)
    {
        ValidateAscii(comment);
        if (start + 2 >= FitsBlock.CardSize)
        {
            throw new FitsException("A header comment does not fit in one card.", new FitsLocation());
        }

        if (image[start] != (byte)' ')
        {
            // Value already ended. Put the slash after a space when one is free.
        }

        int slash = Math.Max(start, 30);
        if (image[slash] != (byte)' ' && slash < FitsBlock.CardSize - 1)
        {
            slash = NextSpace(image, start);
        }

        if (slash + 3 >= FitsBlock.CardSize)
        {
            throw new FitsException("A header comment does not fit in one card.", new FitsLocation());
        }

        image[slash] = (byte)' ';
        image[slash + 1] = (byte)'/';
        image[slash + 2] = (byte)' ';
        WriteBytes(image, slash + 3, comment);
    }

    private static int NextSpace(Span<byte> image, int start)
    {
        for (int i = start; i < FitsBlock.CardSize; i++)
        {
            if (image[i] == (byte)' ')
            {
                return i;
            }
        }

        return FitsBlock.CardSize;
    }

    private static int IndexOfClosingQuote(ReadOnlySpan<byte> image)
    {
        for (int i = 10; i < image.Length; i++)
        {
            if (image[i] != (byte)'\'')
            {
                continue;
            }

            if (i + 1 < image.Length && image[i + 1] == (byte)'\'')
            {
                i++;
                continue;
            }

            if (i > 10)
            {
                return i;
            }
        }

        return -1;
    }

    private static void WriteBytes(Span<byte> image, int start, string text)
    {
        ValidateAscii(text);
        if (start < 0 || text.Length > image.Length - start)
        {
            throw new FitsException("Header text does not fit in one card.", new FitsLocation());
        }

        for (int i = 0; i < text.Length; i++)
        {
            char value = text[i];
            image[start + i] = (byte)value;
        }
    }

    private static void ValidateAscii(string text)
    {
        foreach (char value in text)
        {
            if (value > 0x7F)
            {
                throw new FitsException("FITS card text must contain only ASCII characters.", new FitsLocation());
            }
        }
    }

    private static bool CanFitComment(FitsCard card)
    {
        if (string.IsNullOrEmpty(card.Comment))
        {
            return true;
        }

        ValidateAscii(card.Comment);
        return FitsComment(card.Text ?? "", card.Comment);
    }

    private static bool FitsComment(string value, string comment)
    {
        int slash = Math.Max(30, 12 + EncodedLength(value));
        return comment.Length <= 77 - slash;
    }

    private static string FormatReal(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new FitsException("Header values cannot be NaN or infinity.", new FitsLocation());
        }

        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string FormatComplex(FitsCard card)
    {
        if (double.IsNaN(card.Real) || double.IsInfinity(card.Real) ||
            double.IsNaN(card.Imaginary) || double.IsInfinity(card.Imaginary))
        {
            throw new FitsException("Header values cannot be NaN or infinity.", new FitsLocation(Keyword: card.Keyword));
        }

        var real = card.Real.ToString("R", CultureInfo.InvariantCulture);
        var imaginary = card.Imaginary.ToString("R", CultureInfo.InvariantCulture);
        return $"({real}, {imaginary})";
    }
}
