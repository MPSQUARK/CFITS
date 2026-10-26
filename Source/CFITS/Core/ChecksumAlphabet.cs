using System.Numerics;

namespace CFITS;

/// <summary>
/// FITS checksum characters are ASCII 0-9, A-Z, and a-z.
/// The excluded bands are ':' through '@' and '[' through '`'.
/// </summary>
internal static class ChecksumAlphabet
{
    private const char First = '0';
    private const char Last = 'z';
    private const char ExcludedLowStart = ':';
    private const char ExcludedLowEnd = '@';
    private const char ExcludedHighStart = '[';
    private const char ExcludedHighEnd = '`';

    public static bool Accepts(char character) =>
        (uint)character <= Last
        && character >= First
        && (character < ExcludedLowStart || character > ExcludedLowEnd)
        && (character < ExcludedHighStart || character > ExcludedHighEnd);

    public static bool Accepts(Vector<ushort> characters)
    {
        Vector<ushort> first = new(First);
        Vector<ushort> last = new(Last);
        Vector<ushort> lowStart = new(ExcludedLowStart);
        Vector<ushort> lowEnd = new(ExcludedLowEnd);
        Vector<ushort> highStart = new(ExcludedHighStart);
        Vector<ushort> highEnd = new(ExcludedHighEnd);
        Vector<ushort> inRange = Vector.GreaterThanOrEqual(characters, first) & Vector.LessThanOrEqual(characters, last);
        Vector<ushort> outsideLow = Vector.LessThan(characters, lowStart) | Vector.GreaterThan(characters, lowEnd);
        Vector<ushort> outsideHigh = Vector.LessThan(characters, highStart) | Vector.GreaterThan(characters, highEnd);
        Vector<ushort> accepted = inRange & outsideLow & outsideHigh;
        return Vector.EqualsAll(accepted, ~Vector<ushort>.Zero);
    }
}
