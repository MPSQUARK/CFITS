namespace CFITS.Layout;

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
