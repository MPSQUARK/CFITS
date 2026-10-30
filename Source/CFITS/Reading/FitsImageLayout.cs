namespace CFITS;

/// <summary>
/// Axis lengths of one image HDU.
/// <see cref="Axes"/> is FITS order: NAXIS1 is the fastest index.
/// </summary>
public readonly struct FitsImageLayout
{
    public FitsImageLayout(int[] axes, int bitpix, bool compressed)
    {
        ArgumentNullException.ThrowIfNull(axes);
        Axes = axes;
        Bitpix = bitpix;
        Compressed = compressed;
        PixelCount = Count(axes);
    }

    public IReadOnlyList<int> Axes { get; }

    public int PixelCount { get; }

    public int Bitpix { get; }

    public bool Compressed { get; }

    private static int Count(int[] axes)
    {
        if (axes.Length == 0)
        {
            return 0;
        }

        long count = 1;
        foreach (int axis in axes)
        {
            if (axis == 0)
            {
                return 0;
            }

            count = checked(count * axis);
        }

        if (count > int.MaxValue)
        {
            throw new FitsException("The image has more elements than a single array can hold.", new FitsLocation());
        }

        return (int)count;
    }
}
