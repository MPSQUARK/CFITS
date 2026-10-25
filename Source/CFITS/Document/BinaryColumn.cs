namespace CFITS;

internal sealed class BinaryColumn(string name, string format, int width, int rows, byte[] cells)
{
    public string Name { get; } = name;

    public string Format { get; } = format;

    public int Width { get; } = width;

    public int Rows { get; } = rows;

    public byte[] Cells { get; } = cells;

    public bool Variable { get; init; }

    public bool WideDescriptor { get; init; }

    public byte[] Heap { get; init; } = [];

    public int[] Counts { get; init; } = [];

    public int[] HeapRowLengths { get; init; } = [];

    public string? Dimensions { get; init; }
}
