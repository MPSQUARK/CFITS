namespace CFITS;

/// <summary>FITS block geometry from chapters 3 and 4.</summary>
public static class FitsBlock
{
    public const int Size = 2880;
    public const int CardSize = 80;
    public const int CardsPerBlock = Size / CardSize;

    public static long Pad(long count)
    {
        if (count <= 0)
        {
            return 0;
        }

        long remainder = count % Size;
        return remainder == 0 ? count : count + (Size - remainder);
    }
}
