namespace CFITS;

internal readonly struct ColumnSpec
{
    public char Code { get; init; }

    public char ElementCode { get; init; }

    public int Repeat { get; init; }

    public int Width { get; init; }

    public bool Variable { get; init; }

    public bool Ascii { get; init; }

    public int SubstringWidth { get; init; }

    public int? MaximumElements { get; init; }
}
