namespace CFITS;

internal sealed class AsciiColumn(string name, string format, int width, int rows, string[] cells)
{
    public string Name { get; } = name;

    public string Format { get; } = format;

    public int Width { get; } = width;

    public int Rows { get; } = rows;

    public string[] Cells { get; } = cells;
}
