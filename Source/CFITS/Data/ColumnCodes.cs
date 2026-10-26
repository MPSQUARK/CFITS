namespace CFITS.Data;

/// <summary>
/// Binary-table TFORM type codes. A 128-slot table is a constant-time test
/// for the ASCII range without hashing.
/// </summary>
internal static class ColumnCodes
{
    private static readonly bool[] Known = Build();

    public static bool IsBinaryElement(char code) => (uint)code < (uint)Known.Length && Known[code];

    private static bool[] Build()
    {
        var known = new bool[128];
        ReadOnlySpan<char> codes = ['L', 'X', 'B', 'I', 'J', 'K', 'A', 'E', 'D', 'C', 'M'];
        for (int index = 0; index < codes.Length; index++)
        {
            known[codes[index]] = true;
        }

        return known;
    }
}
