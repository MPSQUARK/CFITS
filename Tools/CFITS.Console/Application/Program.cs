using System.Globalization;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: CFITS.Console <file.fits>");
    return 1;
}

using var document = CFITS.FitsDocument.Open(args[0]);
foreach (var hdu in document.Hdus)
{
    Console.WriteLine($"HDU {hdu.Index} {hdu.Kind}");
    foreach (var card in hdu.Header.Cards)
    {
        Console.WriteLine($"  {card.Keyword,-8} {Describe(card)}");
    }

    if (hdu.Image is { } image)
    {
        Console.WriteLine($"  image BITPIX={image.Bitpix} axes={string.Join('x', image.Axes)} elements={image.Length}");
    }

    if (hdu.Table is { } table)
    {
        Console.WriteLine($"  table rows={table.RowCount} columns={string.Join(", ", table.Columns.Select(column => column.Name))}");
    }

    if (hdu.Groups is { } groups)
    {
        Console.WriteLine($"  groups={groups.Count} parameters={groups.ParameterCount} data={groups.DataLength}");
    }
}

if (!document.SpecialRecords.IsEmpty)
{
    Console.WriteLine($"special records {document.SpecialRecords.Length} bytes");
}

return 0;

static string Describe(CFITS.FitsCard card) => card.Kind switch
{
    CFITS.FitsValueKind.Logical => card.Logical ? "T" : "F",
    CFITS.FitsValueKind.Integer => card.Integer.ToString(CultureInfo.InvariantCulture),
    CFITS.FitsValueKind.Real => card.Real.ToString("R", CultureInfo.InvariantCulture),
    CFITS.FitsValueKind.Complex => $"({card.Real.ToString("R", CultureInfo.InvariantCulture)}, {card.Imaginary.ToString("R", CultureInfo.InvariantCulture)})",
    CFITS.FitsValueKind.String or CFITS.FitsValueKind.Commentary or CFITS.FitsValueKind.Raw => card.Text ?? "",
    _ => "",
};
