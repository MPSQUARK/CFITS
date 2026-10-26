using System.Globalization;

namespace CFITS.Data;

internal static class ColumnFormats
{
    public static ColumnSpec Parse(string format, bool ascii)
    {
        var trimmed = format.Trim().ToUpperInvariant();
        if (ascii)
        {
            return ParseAscii(trimmed);
        }

        int index = 0;
        int repeat = ReadRepeat(trimmed, ref index);
        if (index >= trimmed.Length)
        {
            throw new FitsException($"TFORM '{format}' is missing a type code.", new FitsLocation());
        }

        char code = trimmed[index++];
        if (repeat <= 0)
        {
            throw new FitsException($"TFORM '{format}' must have a positive repeat count.", new FitsLocation());
        }

        if (code is 'P' or 'Q')
        {
            if (index >= trimmed.Length)
            {
                throw new FitsException($"TFORM '{format}' is missing a variable-array element type.", new FitsLocation());
            }

            char element = trimmed[index++];
            if (element is not ('L' or 'X' or 'B' or 'I' or 'J' or 'K' or 'A' or 'E' or 'D' or 'C' or 'M'))
            {
                throw new FitsException($"TFORM '{format}' has an unknown variable-array element type.", new FitsLocation());
            }

            if (repeat != 1)
            {
                throw new FitsException($"TFORM '{format}' has more than one variable-length descriptor.", new FitsLocation());
            }

            int? maximum = null;
            if (index < trimmed.Length)
            {
                if (trimmed[index] != '(' || !trimmed.EndsWith(')'))
                {
                    throw new FitsException($"TFORM '{format}' contains trailing characters.", new FitsLocation());
                }

                string maximumText = trimmed[(index + 1)..^1];
                if (!int.TryParse(maximumText, NumberStyles.None, CultureInfo.InvariantCulture, out int maxElements) || maxElements < 0)
                {
                    throw new FitsException($"TFORM '{format}' has an invalid variable-array limit.", new FitsLocation());
                }

                maximum = maxElements;
            }

            return new ColumnSpec
            {
                Code = code,
                ElementCode = element,
                Repeat = 1,
                Width = code == 'P' ? 8 : 16,
                Variable = true,
                MaximumElements = maximum,
            };
        }

        if (code is not ('L' or 'X' or 'B' or 'I' or 'J' or 'K' or 'A' or 'E' or 'D' or 'C' or 'M'))
        {
            throw new FitsException($"TFORM '{format}' has an unknown type code.", new FitsLocation());
        }

        int substring = 0;
        if (code == 'A' && index < trimmed.Length && char.IsDigit(trimmed[index]))
        {
            substring = ReadRepeat(trimmed, ref index);
            if (substring < 1 || repeat % substring != 0)
            {
                throw new FitsException($"TFORM '{format}' does not divide into equal character fields.", new FitsLocation());
            }
        }

        if (index != trimmed.Length)
        {
            throw new FitsException($"TFORM '{format}' contains trailing characters.", new FitsLocation());
        }

        return new ColumnSpec
        {
            Code = code,
            Repeat = repeat,
            Width = Width(code, repeat),
            SubstringWidth = substring,
        };
    }

    public static int[] ParseDimensions(string text)
    {
        var body = text.Trim();
        if (!body.StartsWith('(') || !body.EndsWith(')'))
        {
            throw new FitsException("TDIM must be enclosed in parentheses.", new FitsLocation());
        }

        if (body.StartsWith('(') && body.EndsWith(')'))
        {
            body = body[1..^1];
        }

        if (body.Length == 0)
        {
            throw new FitsException("TDIM must contain at least one dimension.", new FitsLocation());
        }

        var parts = body.Split(',', StringSplitOptions.None | StringSplitOptions.TrimEntries);
        var axes = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int axis) || axis <= 0)
            {
                throw new FitsException("TDIM dimensions must be positive integers.", new FitsLocation());
            }

            axes[i] = axis;
        }

        return axes;
    }

    public static void ValidateDimensions(int[] dimensions, ColumnSpec spec)
    {
        long product = 1;
        foreach (int dimension in dimensions)
        {
            try
            {
                product = checked(product * dimension);
            }
            catch (OverflowException exception)
            {
                throw new FitsException("TDIM dimensions are too large.", new FitsLocation(), exception);
            }
        }

        if (!spec.Variable && product != spec.Repeat)
        {
            throw new FitsException("TDIM dimensions do not match the column repeat count.", new FitsLocation());
        }
    }

    private static ColumnSpec ParseAscii(string format)
    {
        if (format.Length < 2)
        {
            throw new FitsException($"ASCII TFORM '{format}' is incomplete.", new FitsLocation());
        }

        char code = format[0];
        if (code is not ('A' or 'I' or 'F' or 'E' or 'D'))
        {
            throw new FitsException($"ASCII TFORM '{format}' is not A, I, F, E, or D.", new FitsLocation());
        }

        int dot = format.IndexOf('.');
        int widthEnd = dot > 0 ? dot : format.Length;
        if (!int.TryParse(format[1..widthEnd], NumberStyles.None, CultureInfo.InvariantCulture, out int width) || width <= 0)
        {
            throw new FitsException($"ASCII TFORM '{format}' has an invalid width.", new FitsLocation());
        }

        if (code is 'A' or 'I' && dot >= 0)
        {
            throw new FitsException($"ASCII TFORM '{format}' has an invalid precision.", new FitsLocation());
        }

        if (code is 'F' or 'E' or 'D' && dot < 0)
        {
            throw new FitsException($"ASCII TFORM '{format}' is missing a precision.", new FitsLocation());
        }

        if (dot > 0)
        {
            int end = format.Length;
            int exponent = format.IndexOf('E', dot + 1);
            if (exponent < 0)
                exponent = format.IndexOf('e', dot + 1);
            if (exponent >= 0)
            {
                if (code is not ('E' or 'D')
                    || !int.TryParse(format[(exponent + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int exponentWidth)
                    || exponentWidth <= 0)
                    throw new FitsException($"ASCII TFORM '{format}' has an invalid exponent width.", new FitsLocation());
                end = exponent;
            }

            if (!int.TryParse(format[(dot + 1)..end], NumberStyles.None, CultureInfo.InvariantCulture, out int precision)
                || precision < 0)
                throw new FitsException($"ASCII TFORM '{format}' has an invalid precision.", new FitsLocation());
            if (precision >= width)
                throw new FitsException($"ASCII TFORM '{format}' has a precision wider than its field.", new FitsLocation());
            if (end == format.Length && exponent < 0)
                return new ColumnSpec { Code = code, Repeat = 1, Width = width, Ascii = true };
            if (end != format.Length && exponent < 0)
                throw new FitsException($"ASCII TFORM '{format}' contains trailing characters.", new FitsLocation());
        }

        return new ColumnSpec { Code = code, Repeat = 1, Width = width, Ascii = true };
    }

    private static int ReadRepeat(string format, ref int index)
    {
        int repeat = 0;
        bool any = false;
        while (index < format.Length && char.IsDigit(format[index]))
        {
            any = true;
            try
            {
                repeat = checked((repeat * 10) + (format[index] - '0'));
            }
            catch (OverflowException exception)
            {
                throw new FitsException($"TFORM '{format}' has an oversized repeat count.", new FitsLocation(), exception);
            }
            index++;
        }

        return any ? repeat : 1;
    }

    private static int Width(char code, int repeat) => code switch
    {
        'L' or 'B' or 'A' => repeat > 0 ? repeat : throw InvalidRepeat(),
        'X' => checked((repeat + 7) / 8),
        'I' => checked(repeat * 2),
        'J' or 'E' => checked(repeat * 4),
        'K' or 'D' or 'C' => checked(repeat * 8),
        'M' => checked(repeat * 16),
        _ => throw new FitsException($"Column type {code} has no width.", new FitsLocation()),
    };

    private static FitsException InvalidRepeat() =>
        new("TFORM repeat counts must be positive.", new FitsLocation());
}
