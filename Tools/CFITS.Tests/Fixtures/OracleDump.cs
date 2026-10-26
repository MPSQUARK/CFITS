using System.Globalization;
using System.Text.Json;

namespace CFITS.Tests;

internal static class OracleDump
{
    public static string Create(FitsDocument document)
    {
        var hdus = new List<Dictionary<string, object?>>();
        foreach (var hdu in document.Hdus)
        {
            hdus.Add(new Dictionary<string, object?>
            {
                ["cards"] = hdu.Header.Cards.Select(Card).ToList(),
                ["payload"] = Payload(hdu),
            });
        }

        return JsonSerializer.Serialize(new Dictionary<string, object?> { ["hdus"] = hdus });
    }

    private static Dictionary<string, object?> Card(FitsCard card)
    {
        object? value = card.Kind switch
        {
            FitsValueKind.Logical => card.Logical,
            FitsValueKind.Integer => card.Integer,
            FitsValueKind.Real => FormatFloat(card.Real),
            FitsValueKind.Complex => new object?[] { FormatFloat(card.Real), FormatFloat(card.Imaginary) },
            FitsValueKind.String or FitsValueKind.Commentary or FitsValueKind.Raw => card.Text,
            _ => null,
        };
        string kind = card.Kind switch
        {
            FitsValueKind.Logical => "logical",
            FitsValueKind.Integer => "integer",
            FitsValueKind.Real => "real",
            FitsValueKind.Complex => "complex",
            FitsValueKind.Undefined => "undefined",
            _ => "string",
        };
        return new Dictionary<string, object?>
        {
            ["keyword"] = card.Keyword,
            ["kind"] = kind,
            ["value"] = value,
            ["comment"] = card.Comment ?? "",
        };
    }

    private static object? Payload(FitsHdu hdu) => hdu.Kind switch
    {
        FitsHduKind.Image => hdu.Image!.Length == 0
            ? null
            : new Dictionary<string, object?> { ["image"] = ImageValues(hdu.Image!) },
        FitsHduKind.AsciiTable or FitsHduKind.BinaryTable => new Dictionary<string, object?> { ["columns"] = Columns(hdu.Table!) },
        FitsHduKind.RandomGroups => new Dictionary<string, object?> { ["groups"] = Groups(hdu) },
        _ => null,
    };

    private static List<object?> ImageValues(FitsImage image)
    {
        var values = new List<object?>(image.Length);
        for (int index = 0; index < image.Length; index++)
        {
            double value = image.ReadPhysical(index);
            if (double.IsNaN(value))
            {
                values.Add("nan");
                continue;
            }

            bool exactZero = !double.IsInfinity(image.Zero) && image.Zero == Math.Truncate(image.Zero);
            if (image.Bitpix > 0 && image.Scale == 1 && exactZero)
            {
                Int128 exact = image.ReadInteger(index);
                values.Add(exact >= long.MinValue && exact <= long.MaxValue ? (long)exact : exact.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                values.Add(FormatFloat(value));
            }
        }

        return values;
    }

    private static Dictionary<string, object?> Columns(FitsTable table)
    {
        var columns = new Dictionary<string, object?>();
        foreach (var column in table.Columns)
        {
            var rows = new List<object?>();
            for (int row = 0; row < table.RowCount; row++)
            {
                rows.Add(ColumnValue(column, row));
            }

            columns[column.Name] = rows;
        }

        return columns;
    }

    private static object? ColumnValue(FitsColumn column, int row)
    {
        if (column.Format.Contains('A', StringComparison.Ordinal) && !column.IsVariableLength)
        {
            int count = column.Count(row);
            if (count <= 1)
            {
                return column.IsNull(row) ? null : column.ReadString(row);
            }

            var values = new List<object?>();
            for (int element = 0; element < count; element++)
            {
                values.Add(column.ReadString(row, element));
            }

            return values;
        }

        if (column.IsVariableLength && column.Format.Contains('A', StringComparison.Ordinal))
        {
            return column.ReadString(row);
        }

        char code = VariableCode(column);
        if (code is 'C' or 'M')
        {
            int count = column.Count(row);
            if (count <= 1 && !column.IsVariableLength)
            {
                var complex = column.ReadComplex(row);
                return new object?[] { FormatFloat(complex.Real), FormatFloat(complex.Imaginary) };
            }

            var values = new List<object?>();
            for (int element = 0; element < count; element++)
            {
                var complex = column.ReadComplex(row, element);
                values.Add(new object?[] { FormatFloat(complex.Real), FormatFloat(complex.Imaginary) });
            }

            return values;
        }

        if (code == 'X')
        {
            int count = column.Count(row);
            var bits = new List<object?>();
            for (int element = 0; element < count; element++)
            {
                bits.Add(column.ReadInteger(row, element) != 0);
            }

            return bits;
        }

        if (code == 'L')
        {
            return LogicalRow(column, row);
        }

        int length = column.Count(row);
        if (column.IsVariableLength || length != 1)
        {
            var values = new List<object?>();
            for (int element = 0; element < length; element++)
            {
                values.Add(Scalar(column, row, element, code));
            }

            return column.Dimensions is { Length: > 1 } dimensions ? Shape(dimensions, values) : values;
        }

        return Scalar(column, row, 0, code);
    }

    private static object? Shape(IReadOnlyList<int> dimensions, List<object?> flat)
    {
        int cursor = 0;
        return Nest(dimensions, dimensions.Count - 1, flat, ref cursor);
    }

    private static object? Nest(IReadOnlyList<int> dimensions, int dimension, List<object?> flat, ref int cursor)
    {
        if (dimension < 0)
        {
            return flat[cursor++];
        }

        var nested = new List<object?>();
        for (int index = 0; index < dimensions[dimension]; index++)
        {
            nested.Add(Nest(dimensions, dimension - 1, flat, ref cursor));
        }

        return nested;
    }

    private static object? LogicalRow(FitsColumn column, int row)
    {
        int count = column.Count(row);
        if (count == 1 && !column.IsVariableLength)
        {
            return column.IsNull(row) ? null : column.ReadLogical(row);
        }

        var values = new List<object?>();
        for (int element = 0; element < count; element++)
        {
            values.Add(column.IsNull(row, element) ? null : column.ReadLogical(row, element));
        }

        return values;
    }

    private static object? Scalar(FitsColumn column, int row, int element, char code)
    {
        if (column.IsNull(row, element))
        {
            return null;
        }

        if (code is 'B' or 'I' or 'J' or 'K' or 'X')
        {
            if (code != 'X' && column.Scale == 1)
            {
                Int128 exact = column.ReadExactInteger(row, element);
                return exact >= long.MinValue && exact <= long.MaxValue ? (long)exact : exact.ToString(CultureInfo.InvariantCulture);
            }

            double physical = column.ReadReal(row, element);
            if (column.Scale == 1 && physical == Math.Round(physical) && !double.IsInfinity(physical) && !double.IsNaN(physical))
            {
                return (long)physical;
            }

            return FormatFloat(physical);
        }

        return FormatFloat(column.ReadReal(row, element));
    }

    private static Dictionary<string, object?> Groups(FitsHdu hdu)
    {
        var groups = hdu.Groups ?? throw new InvalidOperationException("Missing groups.");
        var parameters = new Dictionary<string, List<double>>(StringComparer.Ordinal);
        for (int parameter = 0; parameter < groups.ParameterCount; parameter++)
        {
            string name = groups.Names[parameter] ?? $"P{parameter + 1}";
            if (!parameters.TryGetValue(name, out List<double>? values))
            {
                values = [];
                parameters.Add(name, values);
            }

            for (int group = 0; group < groups.Count; group++)
            {
                double value = groups.ReadParameter(group, parameter);
                if (values.Count <= group)
                {
                    values.Add(value);
                }
                else
                {
                    values[group] += value;
                }
            }
        }

        var columns = parameters.ToDictionary(
            pair => pair.Key,
            pair => (object?)pair.Value.Select(FormatFloat).ToList(),
            StringComparer.Ordinal);
        var data = new List<object?>();
        for (int group = 0; group < groups.Count; group++)
        {
            var row = new List<object?>();
            for (int index = 0; index < groups.DataLength; index++)
            {
                row.Add(FormatFloat(groups.ReadData(group, index)));
            }

            data.Add(row);
        }

        columns["DATA"] = data;
        return columns;
    }

    private static char VariableCode(FitsColumn column)
    {
        var format = column.Format.Trim().ToUpperInvariant();
        int index = 0;
        while (index < format.Length && char.IsDigit(format[index]))
        {
            index++;
        }

        if (index >= format.Length)
        {
            return 'A';
        }

        char code = format[index];
        if (code is 'P' or 'Q' && index + 1 < format.Length)
        {
            return format[index + 1];
        }

        return code;
    }

    private static string FormatFloat(double value)
    {
        if (double.IsNaN(value))
        {
            return "nan";
        }

        if (double.IsPositiveInfinity(value))
        {
            return "inf";
        }

        if (double.IsNegativeInfinity(value))
        {
            return "-inf";
        }

        return value.ToString("G17", CultureInfo.InvariantCulture).Replace('E', 'e');
    }
}
