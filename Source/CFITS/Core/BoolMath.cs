using System.Runtime.CompilerServices;

namespace CFITS;

/// <summary>
/// Branchless conversions. The CLR stores <see cref="bool"/> as the byte 0 or 1,
/// so the bit pattern is already the integer value.
/// </summary>
internal static class BoolMath
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ToInt(bool value) => Unsafe.As<bool, byte>(ref Unsafe.AsRef(in value));
}
