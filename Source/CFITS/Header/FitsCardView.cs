using System.Globalization;
using System.Text;

namespace CFITS;

/// <summary>
/// One 80-byte header record, viewed without allocating strings.
/// Analogous to a JSON token over a span.
/// </summary>
public readonly ref struct FitsCardView
{
    private readonly ReadOnlySpan<byte> _image;
    private readonly int _textStart;
    private readonly int _textLength;
    private readonly int _commentStart;
    private readonly int _commentLength;

    private FitsCardView(
        ReadOnlySpan<byte> image,
        FitsValueKind kind,
        bool logical,
        long integer,
        double real,
        double imaginary,
        int textStart,
        int textLength,
        int commentStart,
        int commentLength,
        bool continues)
    {
        _image = image;
        Kind = kind;
        Logical = logical;
        Integer = integer;
        Real = real;
        Imaginary = imaginary;
        _textStart = textStart;
        _textLength = textLength;
        _commentStart = commentStart;
        _commentLength = commentLength;
        Continues = continues;
    }

    public FitsValueKind Kind { get; }

    public bool Logical { get; }

    public long Integer { get; }

    public double Real { get; }

    public double Imaginary { get; }

    public bool Continues { get; }

    public ReadOnlySpan<byte> Image => _image;

    public ReadOnlySpan<byte> Keyword => TrimEnd(_image[..8]);

    public ReadOnlySpan<byte> Text => _image.Slice(_textStart, _textLength);

    public ReadOnlySpan<byte> Comment => _image.Slice(_commentStart, _commentLength);

    public bool IsEnd => Keyword.SequenceEqual("END"u8);

    public bool IsContinue => Keyword.SequenceEqual("CONTINUE"u8);

    public static FitsCardView Parse(ReadOnlySpan<byte> image, long position)
    {
        if (image.Length < FitsBlock.CardSize)
        {
            throw new FitsException("A header record is shorter than 80 bytes.", new FitsLocation(Position: position));
        }

        var card = image[..FitsBlock.CardSize];
        RequirePrintable(card, position);
        var keyword = TrimEnd(card[..8]);
        if (keyword.SequenceEqual("END"u8))
        {
            if (HasNonSpace(card[3..]))
            {
                throw new FitsException("The END card must contain spaces after its keyword.", new FitsLocation(Position: position));
            }

            return new FitsCardView(card, FitsValueKind.Raw, false, 0, 0, 0, 8, 0, 0, 0, false);
        }

        if (IsCommentaryName(keyword))
        {
            var text = TrimEnd(card[8..]);
            return new FitsCardView(card, FitsValueKind.Commentary, false, 0, 0, 0, 8, text.Length, 0, 0, false);
        }

        ValidateKeyword(keyword, position);
        if (card[8] == (byte)'=' && card[9] != (byte)' ')
        {
            throw new FitsException("A value card must use '= ' in columns 9-10.", new FitsLocation(Position: position));
        }

        if (card[8] != (byte)'=')
        {
            if (keyword.SequenceEqual("CONTINUE"u8))
            {
                return ParseContinue(card, position);
            }

            throw new FitsException("A non-commentary card must contain a value indicator.", new FitsLocation(Position: position));
        }

        if (keyword.SequenceEqual("CONTINUE"u8))
        {
            throw new FitsException("A CONTINUE card must use the long-string card form.", new FitsLocation(Position: position));
        }

        return ParseValue(card, position);
    }

    public string KeywordText() => Ascii(Keyword);

    public string? TextValue()
    {
        if (Kind is FitsValueKind.Commentary or FitsValueKind.Raw)
        {
            return Ascii(Text);
        }

        if (Kind != FitsValueKind.String)
        {
            return null;
        }

        return DecodeQuoted(Text, Continues).Value;
    }

    public string CommentText() => Ascii(Comment);

    public string? UnitText()
    {
        var comment = Comment;
        if (comment.Length < 2 || comment[0] != (byte)'[')
        {
            return null;
        }

        int close = comment.IndexOf((byte)']');
        if (close <= 1)
        {
            return null;
        }

        return Ascii(comment[1..close]);
    }

    internal static bool IsCommentaryName(ReadOnlySpan<byte> keyword)
    {
        return keyword.IsEmpty
            || keyword.SequenceEqual("COMMENT"u8)
            || keyword.SequenceEqual("HISTORY"u8);
    }

    internal static (string Value, bool Continues) DecodeQuoted(ReadOnlySpan<byte> interior, bool alreadyFlagged)
    {
        int end = interior.Length;
        while (end > 0 && interior[end - 1] == (byte)' ')
        {
            end--;
        }

        var trimmed = interior[..end];
        bool continues = alreadyFlagged;
        if (trimmed.Length > 0 && trimmed[^1] == (byte)'&')
        {
            trimmed = trimmed[..^1];
            continues = true;
        }

        return (Undouble(trimmed), continues);
    }

    private static FitsCardView ParseContinue(ReadOnlySpan<byte> card, long position)
    {
        int index = SkipSpaces(card, 10);
        if (index >= FitsBlock.CardSize || card[index] != (byte)'\'')
        {
            throw new FitsException("A CONTINUE card must contain a quoted string.", new FitsLocation(Position: position));
        }

        var quoted = ReadQuoted(card, index, position);
        var decoded = DecodeQuoted(card.Slice(quoted.Start, quoted.Length), false);
        int commentAt = SkipSpaces(card, quoted.Next);
        RequireValueEnd(card, commentAt, position);
        var comment = ReadComment(card, commentAt);
        return new FitsCardView(
            card,
            FitsValueKind.String,
            false,
            0,
            0,
            0,
            quoted.Start,
            quoted.Length,
            comment.Start,
            comment.Length,
            decoded.Continues);
    }

    private static FitsCardView ParseValue(ReadOnlySpan<byte> card, long position)
    {
        int index = SkipSpaces(card, 10);
        if (index >= FitsBlock.CardSize || card[index] == (byte)'/')
        {
            var comment = ReadComment(card, index);
            return new FitsCardView(card, FitsValueKind.Undefined, false, 0, 0, 0, 0, 0, comment.Start, comment.Length, false);
        }

        if (card[index] == (byte)'\'')
        {
            var quoted = ReadQuoted(card, index, position);
            var decoded = DecodeQuoted(card.Slice(quoted.Start, quoted.Length), false);
            int commentAt = SkipSpaces(card, quoted.Next);
            RequireValueEnd(card, commentAt, position);
            var comment = ReadComment(card, commentAt);
            return new FitsCardView(
                card,
                FitsValueKind.String,
                false,
                0,
                0,
                0,
                quoted.Start,
                quoted.Length,
                comment.Start,
                comment.Length,
                decoded.Continues);
        }

        if (card[index] == (byte)'(')
        {
            return ParseComplex(card, index, position);
        }

        if (IsLogicalToken(card, index))
        {
            int commentAt = SkipSpaces(card, index + 1);
            RequireValueEnd(card, commentAt, position);
            var comment = ReadComment(card, commentAt);
            return new FitsCardView(
                card,
                FitsValueKind.Logical,
                card[index] == (byte)'T',
                0,
                0,
                0,
                0,
                0,
                comment.Start,
                comment.Length,
                false);
        }

        var number = ReadNumber(card, index, position);
        int numberCommentAt = SkipSpaces(card, number.Next);
        RequireValueEnd(card, numberCommentAt, position);
        var numberComment = ReadComment(card, numberCommentAt);
        return new FitsCardView(
            card,
            number.IsInteger ? FitsValueKind.Integer : FitsValueKind.Real,
            false,
            number.Integer,
            number.Real,
            0,
            0,
            0,
            numberComment.Start,
            numberComment.Length,
            false);
    }

    private static FitsCardView ParseComplex(ReadOnlySpan<byte> card, int index, long position)
    {
        int cursor = SkipSpaces(card, index + 1);
        var real = ReadNumber(card, cursor, position);
        cursor = SkipSpaces(card, real.Next);
        if (cursor >= FitsBlock.CardSize || card[cursor] != (byte)',')
        {
            throw new FitsException("A complex header value is missing its comma.", new FitsLocation(Position: position));
        }

        cursor = SkipSpaces(card, cursor + 1);
        var imaginary = ReadNumber(card, cursor, position);
        cursor = SkipSpaces(card, imaginary.Next);
        if (cursor >= FitsBlock.CardSize || card[cursor] != (byte)')')
        {
            throw new FitsException("A complex header value is missing its closing parenthesis.", new FitsLocation(Position: position));
        }

        var comment = ReadComment(card, SkipSpaces(card, cursor + 1));
        double realValue = real.IsInteger ? real.Integer : real.Real;
        double imaginaryValue = imaginary.IsInteger ? imaginary.Integer : imaginary.Real;
        RequireValueEnd(card, SkipSpaces(card, cursor + 1), position);
        return new FitsCardView(card, FitsValueKind.Complex, false, 0, realValue, imaginaryValue, 0, 0, comment.Start, comment.Length, false);
    }

    private static bool IsLogicalToken(ReadOnlySpan<byte> card, int index)
    {
        byte mark = card[index];
        if (mark is not ((byte)'T' or (byte)'F'))
        {
            return false;
        }

        int next = index + 1;
        return next >= FitsBlock.CardSize || card[next] is (byte)' ' or (byte)'/';
    }

    private readonly struct Quoted
    {
        public int Start { get; init; }
        public int Length { get; init; }
        public int Next { get; init; }
    }

    private static Quoted ReadQuoted(ReadOnlySpan<byte> card, int opening, long position)
    {
        int index = opening + 1;
        while (index < FitsBlock.CardSize)
        {
            if (card[index] != (byte)'\'')
            {
                index++;
                continue;
            }

            if (index + 1 < FitsBlock.CardSize && card[index + 1] == (byte)'\'')
            {
                index += 2;
                continue;
            }

            return new Quoted
            {
                Start = opening + 1,
                Length = index - opening - 1,
                Next = index + 1,
            };
        }

        throw new FitsException("A quoted header value has no closing quote.", new FitsLocation(Position: position));
    }

    private readonly struct NumberToken
    {
        public bool IsInteger { get; init; }
        public long Integer { get; init; }
        public double Real { get; init; }
        public int Next { get; init; }
    }

    private static NumberToken ReadNumber(ReadOnlySpan<byte> card, int start, long position)
    {
        int index = start;
        if (index < FitsBlock.CardSize && card[index] is (byte)'+' or (byte)'-')
        {
            index++;
        }

        int digits = index;
        bool hasDigits = false;
        bool dot = false;
        bool exponent = false;
        while (index < FitsBlock.CardSize)
        {
            byte current = card[index];
            if (current is >= (byte)'0' and <= (byte)'9')
            {
                hasDigits = true;
                index++;
                continue;
            }

            if (current == (byte)'.' && !dot && !exponent)
            {
                dot = true;
                index++;
                continue;
            }

            if (current is (byte)'E' or (byte)'D')
            {
                if (exponent || !hasDigits)
                {
                    break;
                }

                exponent = true;
                index++;
                if (index < FitsBlock.CardSize && card[index] is (byte)'+' or (byte)'-')
                {
                    index++;
                }

                int exponentDigits = index;
                while (index < FitsBlock.CardSize && card[index] is >= (byte)'0' and <= (byte)'9')
                {
                    index++;
                }

                if (index == exponentDigits)
                {
                    throw new FitsException("A header exponent must contain digits.", new FitsLocation(Position: position));
                }

                continue;
            }

            break;
        }

        if (index == digits || !hasDigits)
        {
            throw new FitsException("A header value is not a logical, number, string, or complex value.", new FitsLocation(Position: position));
        }

        var token = card[start..index];
        if (!dot && !exponent)
        {
            if (long.TryParse(Encoding.ASCII.GetString(token), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long integer))
            {
                return new NumberToken { IsInteger = true, Integer = integer, Real = integer, Next = index };
            }

            throw new FitsException("A header integer is outside the range of a 64-bit value.", new FitsLocation(Position: position));
        }

        Span<char> chars = stackalloc char[token.Length];
        for (int i = 0; i < token.Length; i++)
        {
            byte current = token[i];
            chars[i] = current is (byte)'D' or (byte)'d' ? 'E' : (char)current;
        }

        if (!double.TryParse(chars, NumberStyles.Float, CultureInfo.InvariantCulture, out double real)
            || double.IsNaN(real)
            || double.IsInfinity(real))
        {
            throw new FitsException("A header number is outside the range of a 64-bit value.", new FitsLocation(Position: position));
        }

        return new NumberToken { IsInteger = false, Real = real, Next = index };
    }

    private readonly struct CommentSpan
    {
        public int Start { get; init; }
        public int Length { get; init; }
    }

    private static CommentSpan ReadComment(ReadOnlySpan<byte> card, int index)
    {
        if (index >= FitsBlock.CardSize || card[index] != (byte)'/')
        {
            return default;
        }

        int start = index + 1;
        if (start < FitsBlock.CardSize && card[start] == (byte)' ')
        {
            start++;
        }

        int end = FitsBlock.CardSize;
        while (end > start && card[end - 1] == (byte)' ')
        {
            end--;
        }

        return new CommentSpan { Start = start, Length = Math.Max(0, end - start) };
    }

    private static void RequireValueEnd(ReadOnlySpan<byte> card, int index, long position)
    {
        if (index < FitsBlock.CardSize && card[index] != (byte)'/')
        {
            throw new FitsException("Unexpected characters follow a header value.", new FitsLocation(Position: position));
        }

        if (index < FitsBlock.CardSize && index > 0 && card[index - 1] != (byte)' ')
        {
            throw new FitsException("A header comment delimiter must be separated from its value.", new FitsLocation(Position: position));
        }

    }

    private static int SkipSpaces(ReadOnlySpan<byte> card, int index)
    {
        while (index < FitsBlock.CardSize && card[index] == (byte)' ')
        {
            index++;
        }

        return index;
    }

    private static ReadOnlySpan<byte> TrimEnd(ReadOnlySpan<byte> value)
    {
        int end = value.Length;
        while (end > 0 && value[end - 1] == (byte)' ')
        {
            end--;
        }

        return value[..end];
    }

    private static bool HasNonSpace(ReadOnlySpan<byte> value)
    {
        foreach (byte current in value)
        {
            if (current != (byte)' ')
            {
                return true;
            }
        }

        return false;
    }

    private static string Undouble(ReadOnlySpan<byte> interior)
    {
        if (interior.IndexOf((byte)'\'') < 0)
        {
            return Ascii(interior);
        }

        Span<char> chars = stackalloc char[interior.Length];
        int count = 0;
        for (int i = 0; i < interior.Length; i++)
        {
            chars[count++] = (char)interior[i];
            if (interior[i] == (byte)'\'' && i + 1 < interior.Length && interior[i + 1] == (byte)'\'')
            {
                i++;
            }
        }

        return new string(chars[..count]);
    }

    private static string Ascii(ReadOnlySpan<byte> bytes)
    {
        return Encoding.ASCII.GetString(bytes);
    }

    private static void ValidateKeyword(ReadOnlySpan<byte> keyword, long position)
    {
        if (keyword.IsEmpty || keyword.Length > 8)
        {
            throw new FitsException("A standard keyword must contain 1 to 8 characters.", new FitsLocation(Position: position));
        }

        foreach (byte current in keyword)
        {
            if (current is not (>= (byte)'A' and <= (byte)'Z')
                and not (>= (byte)'0' and <= (byte)'9')
                and not (byte)'_'
                and not (byte)'-')
            {
                throw new FitsException("A keyword contains a character the FITS standard does not allow.", new FitsLocation(Position: position));
            }
        }
    }

    private static void RequirePrintable(ReadOnlySpan<byte> card, long position)
    {
        foreach (byte current in card)
        {
            if (current is < 32 or > 126)
            {
                throw new FitsException("A header record contains a character outside ASCII 32-126.", new FitsLocation(Position: position));
            }
        }
    }
}
