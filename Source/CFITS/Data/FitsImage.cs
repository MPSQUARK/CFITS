using System.Numerics;
using System.Runtime.InteropServices;

namespace CFITS;

/// <summary>An image array in FITS axis order: the first axis varies fastest.</summary>
public sealed class FitsImage
{
    private readonly ReadOnlyMemory<byte> _raw;
    private readonly FitsScaleMode _mode;
    private readonly bool _integer;

    private FitsImage(ReadOnlyMemory<byte> raw, int bitpix, int[] axes, double scale, double zero, long? blank, FitsScaleMode mode)
    {
        _raw = raw;
        _mode = mode;
        Bitpix = bitpix;
        Axes = axes;
        Scale = scale;
        Zero = zero;
        Blank = blank;
        _integer = bitpix > 0;
        long count = 1;
        foreach (int axis in axes)
        {
            count = checked(count * axis);
        }

        if (axes.Length == 0 || HasZeroAxis(axes))
        {
            count = 0;
        }

        if (count > int.MaxValue)
        {
            throw new FitsException("The image has more elements than a single array can hold.", new FitsLocation());
        }

        Length = (int)count;
        long expectedBytes = checked(count * (long)Pixels.Width(bitpix));
        if (raw.Length != expectedBytes)
        {
            throw new FitsException("The image data length does not match the declared axes.", new FitsLocation());
        }
    }

    public int Bitpix { get; }

    public IReadOnlyList<int> Axes { get; }

    public double Scale { get; }

    public double Zero { get; }

    public long? Blank { get; }

    public int Length { get; }

    internal ReadOnlyMemory<byte> Stored => _raw;

    public void CopyStoredInt32(Span<int> destination)
    {
        if (Bitpix != 32)
        {
            throw new FitsException("CopyStoredInt32 requires BITPIX 32.", new FitsLocation(Keyword: "BITPIX"));
        }

        if (destination.Length < Length)
        {
            throw new FitsException("The destination is shorter than the image.", new FitsLocation());
        }

        Pixels.CopyStored(_raw.Span, Bitpix, Length, destination);
    }

    public void CopyInteger(Span<int> destination)
    {
        EnsureIntegerDestination(destination);
        Pixels.CopyStored(_raw.Span, Bitpix, Length, destination);
    }

    public void CopyInteger(Span<short> destination)
    {
        EnsureIntegerDestination(destination);
        Pixels.CopyStored(_raw.Span, Bitpix, Length, destination);
    }

    public void CopyInteger(Span<long> destination)
    {
        EnsureIntegerDestination(destination);
        Pixels.CopyStored(_raw.Span, Bitpix, Length, destination);
    }

    public void CopyInteger(Span<byte> destination)
    {
        EnsureIntegerDestination(destination);
        Pixels.CopyStored(_raw.Span, Bitpix, Length, destination);
    }

    public void CopyInteger(short[,] destination) => CopyIntegerPlane(destination, 16);

    public void CopyInteger(int[,] destination) => CopyIntegerPlane(destination, 32);

    public void CopyInteger(long[,] destination) => CopyIntegerPlane(destination, 64);

    public void CopyInteger(byte[,] destination) => CopyIntegerPlane(destination, 8);

    public void CopyInteger(short[,,] destination) => CopyIntegerVolume(destination);

    public void CopyInteger(int[,,] destination) => CopyIntegerVolume(destination);

    public void CopyInteger(long[,,] destination) => CopyIntegerVolume(destination);

    public void CopyInteger(byte[,,] destination) => CopyIntegerVolume(destination);

    private void CopyIntegerPlane<T>(T[,] destination, int storedBits)
        where T : unmanaged, INumber<T>
    {
        ValidateRectangularShape(destination.GetLength(0), destination.GetLength(1), 0, 2);
        EnsureIntegerDestination(destination.Length);
        if (CanCopyStoredBits(storedBits))
        {
            CopyStoredPlane(destination, storedBits);
            return;
        }

        int width = destination.GetLength(1);
        int row = 0;
        int column = 0;
        for (int index = 0; index < Length; index++)
        {
            destination[row, column] = T.CreateTruncating(ReadInteger(index));
            column++;
            if (column == width)
            {
                column = 0;
                row++;
            }
        }
    }

    private void CopyStoredPlane<T>(T[,] destination, int storedBits)
        where T : unmanaged
    {
        if (storedBits == 8)
        {
            _raw.Span[..Length].CopyTo(MemoryMarshal.AsBytes(FitsEndian.AsContiguous(destination))[..Length]);
            return;
        }

        if (storedBits == 16)
        {
            FitsEndian.ReadInt16(_raw.Span[..(Length * sizeof(short))], MemoryMarshal.Cast<T, short>(FitsEndian.AsContiguous(destination))[..Length]);
            return;
        }

        if (storedBits == 32)
        {
            FitsEndian.ReadInt32(_raw.Span[..(Length * sizeof(int))], MemoryMarshal.Cast<T, int>(FitsEndian.AsContiguous(destination))[..Length]);
            return;
        }

        FitsEndian.ReadInt64(_raw.Span[..(Length * sizeof(long))], MemoryMarshal.Cast<T, long>(FitsEndian.AsContiguous(destination))[..Length]);
    }

    private void CopyIntegerVolume<T>(T[,,] destination)
        where T : unmanaged, INumber<T>
    {
        ValidateRectangularShape(destination.GetLength(0), destination.GetLength(1), destination.GetLength(2), 3);
        EnsureIntegerDestination(destination.Length);
        int plane = destination.GetLength(1);
        int row = destination.GetLength(2);
        int z = 0;
        int y = 0;
        int x = 0;
        for (int index = 0; index < Length; index++)
        {
            destination[z, y, x] = T.CreateTruncating(ReadInteger(index));
            x++;
            if (x == row)
            {
                x = 0;
                y++;
                if (y == plane)
                {
                    y = 0;
                    z++;
                }
            }
        }
    }

    public void CopyPhysical(Span<double> destination)
    {
        if (destination.Length < Length)
        {
            throw new FitsException("The destination is shorter than the image.", new FitsLocation());
        }

        bool physical = _mode == FitsScaleMode.Physical;
        Pixels.Copy(
            _raw.Span,
            Bitpix,
            Length,
            physical ? Scale : 1,
            physical ? Zero : 0,
            physical ? Blank : null,
            destination);
    }

    public void CopyPhysical(Span<float> destination)
    {
        if (destination.Length < Length)
        {
            throw new FitsException("The destination is shorter than the image.", new FitsLocation());
        }

        bool physical = _mode == FitsScaleMode.Physical;
        Pixels.Copy(
            _raw.Span,
            Bitpix,
            Length,
            physical ? Scale : 1,
            physical ? Zero : 0,
            physical ? Blank : null,
            destination);
    }

    public void CopyPhysical(float[,] destination)
    {
        ValidateRectangularShape(destination.GetLength(0), destination.GetLength(1), 0, 2);
        int n1 = Axes[0];
        bool physical = _mode == FitsScaleMode.Physical;
        if (Bitpix == -32 && (!physical || (Scale == 1 && Zero == 0 && Blank is null)))
        {
            FitsEndian.ReadSingle(_raw.Span[..(Length * sizeof(float))], FitsEndian.AsContiguous(destination)[..Length]);
            return;
        }

        Pixels.Copy(
            _raw.Span,
            Bitpix,
            Length,
            physical ? Scale : 1,
            physical ? Zero : 0,
            physical ? Blank : null,
            destination,
            n1);
    }

    public void CopyPhysical(float[,,] destination)
    {
        ValidateRectangularShape(destination.GetLength(0), destination.GetLength(1), destination.GetLength(2), 3);
        int n1 = Axes[0];
        int n2 = Axes[1];
        Pixels.Copy(
            _raw.Span,
            Bitpix,
            Length,
            _mode == FitsScaleMode.Physical ? Scale : 1,
            _mode == FitsScaleMode.Physical ? Zero : 0,
            _mode == FitsScaleMode.Physical ? Blank : null,
            destination,
            n1,
            n2);
    }

    public void CopyPhysical(double[,] destination)
    {
        ValidateRectangularShape(destination.GetLength(0), destination.GetLength(1), 0, 2);
        int n1 = Axes[0];
        Pixels.Copy(
            _raw.Span,
            Bitpix,
            Length,
            _mode == FitsScaleMode.Physical ? Scale : 1,
            _mode == FitsScaleMode.Physical ? Zero : 0,
            _mode == FitsScaleMode.Physical ? Blank : null,
            destination,
            n1);
    }

    public void CopyPhysical(double[,,] destination)
    {
        ValidateRectangularShape(destination.GetLength(0), destination.GetLength(1), destination.GetLength(2), 3);
        int n1 = Axes[0];
        int n2 = Axes[1];
        Pixels.Copy(
            _raw.Span,
            Bitpix,
            Length,
            _mode == FitsScaleMode.Physical ? Scale : 1,
            _mode == FitsScaleMode.Physical ? Zero : 0,
            _mode == FitsScaleMode.Physical ? Blank : null,
            destination,
            n1,
            n2);
    }

    public double SumPhysical()
    {
        bool physical = _mode == FitsScaleMode.Physical;
        return Pixels.Sum(
            _raw.Span,
            Bitpix,
            Length,
            physical ? Scale : 1,
            physical ? Zero : 0,
            physical ? Blank : null);
    }

    public double[] ReadPhysical()
    {
        var values = new double[Length];
        CopyPhysical(values);
        return values;
    }

    public long ReadStoredInteger(int index)
    {
        ValidateIndex(index);
        return Pixels.ReadInteger(_raw.Span, Bitpix, index);
    }

    public Int128 ReadInteger(int index)
    {
        if (!_integer)
        {
            throw new FitsException("This image does not store integers.", new FitsLocation(Keyword: "BITPIX"));
        }

        ValidateIndex(index);
        long stored = ReadStoredInteger(index);
        if (Blank is long marker && stored == marker)
        {
            throw new FitsException("The pixel is blank.", new FitsLocation());
        }

        if (_mode == FitsScaleMode.Stored)
        {
            return stored;
        }

        if (Scale == 1 && Pixels.IsExactInteger(Zero))
        {
            return checked((Int128)stored + Pixels.ToInt128(Zero));
        }

        return Pixels.ToInt128((Scale * stored) + Zero);
    }

    public double ReadPhysical(int index)
    {
        ValidateIndex(index);
        double stored = Pixels.Read(_raw.Span, Bitpix, index);
        if (_mode == FitsScaleMode.Stored)
        {
            return stored;
        }

        long storedInteger = _integer ? Pixels.ReadInteger(_raw.Span, Bitpix, index) : 0;
        return Pixels.Physical(stored, storedInteger, _integer, Blank, Scale, Zero);
    }

    private void EnsureIntegerDestination<T>(Span<T> destination)
    {
        if (!_integer)
        {
            throw new FitsException("This image does not store integers.", new FitsLocation(Keyword: "BITPIX"));
        }

        if (destination.Length < Length)
        {
            throw new FitsException("The destination is shorter than the image.", new FitsLocation());
        }
    }

    private void EnsureIntegerDestination(int destinationLength)
    {
        if (!_integer)
        {
            throw new FitsException("This image does not store integers.", new FitsLocation(Keyword: "BITPIX"));
        }

        if (destinationLength < Length)
        {
            throw new FitsException("The destination is shorter than the image.", new FitsLocation());
        }
    }

    private void ValidateIndex(int index)
    {
        if ((uint)index >= (uint)Length)
        {
            throw new FitsException("The pixel index is outside the image.", new FitsLocation());
        }
    }

    private static bool HasZeroAxis(int[] axes)
    {
        for (int index = 0; index < axes.Length; index++)
        {
            if (axes[index] == 0)
            {
                return true;
            }
        }

        return false;
    }

    private bool CanCopyStoredBits(int bitpix) =>
        Bitpix == bitpix && (_mode == FitsScaleMode.Stored || (Blank is null && Scale == 1 && Zero == 0));

    private void ValidateRectangularShape(int first, int second, int third, int rank)
    {
        bool valid = Axes.Count == rank
            && (rank != 2 || (first == Axes[1] && second == Axes[0]))
            && (rank != 3 || (first == Axes[2] && second == Axes[1] && third == Axes[0]));
        if (!valid)
        {
            throw new FitsException("The destination shape does not match the image.", new FitsLocation());
        }
    }

    internal static FitsImage Create(FitsHeader header, ReadOnlyMemory<byte> raw, HduShape shape, FitsScaleMode mode)
    {
        double scale = header.TryGetCard("BSCALE", out var scaleCard) ? scaleCard.GetDouble() : 1;
        double zero = header.TryGetCard("BZERO", out var zeroCard) ? zeroCard.GetDouble() : 0;
        long? blank = header.TryGetCard("BLANK", out var blankCard) && blankCard.Kind == FitsValueKind.Integer
            ? blankCard.Integer
            : null;
        return new FitsImage(raw, shape.Bitpix, shape.Axes, scale, zero, blank, mode);
    }

    internal static FitsImage Create(ReadOnlyMemory<byte> raw, HduShape shape, double scale, double zero, long? blank, FitsScaleMode mode) =>
        new(raw, shape.Bitpix, shape.Axes, scale, zero, blank, mode);
}
