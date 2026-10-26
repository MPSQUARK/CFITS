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

        if (axes.Length == 0 || axes.Any(static axis => axis == 0))
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

    public void CopyInteger(short[,] destination)
    {
        ValidateRectangularShape(destination.GetLength(0), destination.GetLength(1), 0, 2);
        EnsureIntegerDestination(destination.Length);
        if (CanCopyStoredBits(16))
        {
            FitsEndian.ReadInt16(_raw.Span[..(Length * sizeof(short))], FitsEndian.AsContiguous(destination)[..Length]);
            return;
        }

        int width = destination.GetLength(1);
        for (int index = 0, row = 0, column = 0; index < Length; index++)
        {
            destination[row, column] = (short)ReadInteger(index);
            if (++column == width)
            {
                column = 0;
                row++;
            }
        }
    }

    public void CopyInteger(int[,] destination)
    {
        ValidateRectangularShape(destination.GetLength(0), destination.GetLength(1), 0, 2);
        EnsureIntegerDestination(destination.Length);
        if (CanCopyStoredBits(32))
        {
            FitsEndian.ReadInt32(_raw.Span[..(Length * sizeof(int))], FitsEndian.AsContiguous(destination)[..Length]);
            return;
        }

        int width = destination.GetLength(1);
        for (int index = 0, row = 0, column = 0; index < Length; index++)
        {
            destination[row, column] = (int)ReadInteger(index);
            if (++column == width)
            {
                column = 0;
                row++;
            }
        }
    }

    public void CopyInteger(long[,] destination)
    {
        ValidateRectangularShape(destination.GetLength(0), destination.GetLength(1), 0, 2);
        EnsureIntegerDestination(destination.Length);
        if (CanCopyStoredBits(64))
        {
            FitsEndian.ReadInt64(_raw.Span[..(Length * sizeof(long))], FitsEndian.AsContiguous(destination)[..Length]);
            return;
        }

        int width = destination.GetLength(1);
        for (int index = 0, row = 0, column = 0; index < Length; index++)
        {
            destination[row, column] = (long)ReadInteger(index);
            if (++column == width)
            {
                column = 0;
                row++;
            }
        }
    }

    public void CopyInteger(byte[,] destination)
    {
        ValidateRectangularShape(destination.GetLength(0), destination.GetLength(1), 0, 2);
        EnsureIntegerDestination(destination.Length);
        if (CanCopyStoredBits(8))
        {
            _raw.Span[..Length].CopyTo(FitsEndian.AsContiguous(destination)[..Length]);
            return;
        }

        int width = destination.GetLength(1);
        for (int index = 0, row = 0, column = 0; index < Length; index++)
        {
            destination[row, column] = (byte)ReadInteger(index);
            if (++column == width)
            {
                column = 0;
                row++;
            }
        }
    }

    public void CopyInteger(short[,,] destination)
    {
        ValidateRectangularShape(destination.GetLength(0), destination.GetLength(1), destination.GetLength(2), 3);
        EnsureIntegerDestination(destination.Length);
        for (int index = 0; index < Length; index++)
            destination[index / (destination.GetLength(1) * destination.GetLength(2)),
                (index / destination.GetLength(2)) % destination.GetLength(1), index % destination.GetLength(2)] = (short)ReadInteger(index);
    }

    public void CopyInteger(int[,,] destination)
    {
        ValidateRectangularShape(destination.GetLength(0), destination.GetLength(1), destination.GetLength(2), 3);
        EnsureIntegerDestination(destination.Length);
        for (int index = 0; index < Length; index++)
            destination[index / (destination.GetLength(1) * destination.GetLength(2)),
                (index / destination.GetLength(2)) % destination.GetLength(1), index % destination.GetLength(2)] = (int)ReadInteger(index);
    }

    public void CopyInteger(long[,,] destination)
    {
        ValidateRectangularShape(destination.GetLength(0), destination.GetLength(1), destination.GetLength(2), 3);
        EnsureIntegerDestination(destination.Length);
        for (int index = 0; index < Length; index++)
            destination[index / (destination.GetLength(1) * destination.GetLength(2)),
                (index / destination.GetLength(2)) % destination.GetLength(1), index % destination.GetLength(2)] = (long)ReadInteger(index);
    }

    public void CopyInteger(byte[,,] destination)
    {
        ValidateRectangularShape(destination.GetLength(0), destination.GetLength(1), destination.GetLength(2), 3);
        EnsureIntegerDestination(destination.Length);
        for (int index = 0; index < Length; index++)
            destination[index / (destination.GetLength(1) * destination.GetLength(2)),
                (index / destination.GetLength(2)) % destination.GetLength(1), index % destination.GetLength(2)] = (byte)ReadInteger(index);
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
}
