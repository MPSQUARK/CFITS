namespace CFITS;

/// <summary>Describes one legal ASCII or binary table column.</summary>
public sealed class FitsTableColumnSchema
{
    public FitsTableColumnSchema(string name, string format, int? start = null, string? dimensions = null)
    {
        Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("A column name is required.", nameof(name)) : name;
        Format = string.IsNullOrWhiteSpace(format) ? throw new ArgumentException("A TFORM value is required.", nameof(format)) : format;
        Start = start;
        Dimensions = dimensions;
    }

    public string Name { get; }

    public string Format { get; }

    public int? Start { get; }

    public string? Dimensions { get; }
}
