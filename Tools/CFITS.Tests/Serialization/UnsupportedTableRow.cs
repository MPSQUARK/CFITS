namespace CFITS.Tests;

[FitsTable("UNSUPPORTED_ROWS")]
public sealed class UnsupportedTableRow
{
    [FitsColumn("VALUE")]
    public DateTime Value { get; set; }
}
