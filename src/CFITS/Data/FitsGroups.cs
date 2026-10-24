namespace CFITS;

/// <summary>Random-groups records from chapter 6. Parameters precede each data array.</summary>
public sealed class FitsGroups
{
    private readonly ReadOnlyMemory<byte> _raw;
    private readonly int _bitpix;
    private readonly int _width;
    private readonly int _stride;
    private readonly double _scale;
    private readonly double _zero;
    private readonly long? _blank;
    private readonly double[] _parameterScale;
    private readonly double[] _parameterZero;
    private readonly FitsScaleMode _mode;

    private FitsGroups(
        ReadOnlyMemory<byte> raw,
        int bitpix,
        int parameters,
        int dataLength,
        int groups,
        double scale,
        double zero,
        long? blank,
        double[] parameterScale,
        double[] parameterZero,
        string?[] names,
        FitsScaleMode mode)
    {
        _raw = raw;
        _bitpix = bitpix;
        _width = Pixels.Width(bitpix);
        ParameterCount = parameters;
        DataLength = dataLength;
        Count = groups;
        _stride = parameters + dataLength;
        _scale = scale;
        _zero = zero;
        _blank = blank;
        _parameterScale = parameterScale;
        _parameterZero = parameterZero;
        Names = names;
        _mode = mode;
    }

    public int Count { get; }

    public int ParameterCount { get; }

    public int DataLength { get; }

    public IReadOnlyList<string?> Names { get; }

    public double ReadParameter(int group, int parameter)
    {
        double stored = ReadAt((group * _stride) + parameter);
        if (_mode == FitsScaleMode.Stored)
        {
            return stored;
        }

        return _parameterZero[parameter] + (_parameterScale[parameter] * stored);
    }

    public double ReadData(int group, int index)
    {
        int at = (group * _stride) + ParameterCount + index;
        double stored = ReadAt(at);
        if (_mode == FitsScaleMode.Stored)
        {
            return stored;
        }

        long storedInteger = _bitpix > 0 ? Pixels.ReadInteger(_raw.Span, _bitpix, at) : 0;
        return Pixels.Physical(stored, storedInteger, _bitpix > 0, _blank, _scale, _zero);
    }

    internal static FitsGroups Create(FitsHeader header, ReadOnlyMemory<byte> raw, HduShape shape, FitsScaleMode mode)
    {
        int parameters = checked((int)shape.Pcount);
        int dataLength = 1;
        if (shape.Axes.Length <= 1)
        {
            dataLength = 0;
        }
        else
        {
            dataLength = 1;
            for (int i = 1; i < shape.Axes.Length; i++)
            {
                dataLength = checked(dataLength * shape.Axes[i]);
            }
        }

        var scales = new double[parameters];
        var zeros = new double[parameters];
        var names = new string?[parameters];
        for (int i = 0; i < parameters; i++)
        {
            scales[i] = header.TryGetCard($"PSCAL{i + 1}", out var scale) ? scale.GetDouble() : 1;
            zeros[i] = header.TryGetCard($"PZERO{i + 1}", out var zero) ? zero.GetDouble() : 0;
            names[i] = header.TryGetCard($"PTYPE{i + 1}", out var type) ? type.GetString() : null;
        }

        double bscale = header.TryGetCard("BSCALE", out var imageScale) ? imageScale.GetDouble() : 1;
        double bzero = header.TryGetCard("BZERO", out var imageZero) ? imageZero.GetDouble() : 0;
        long? blank = header.TryGetCard("BLANK", out var blankCard) && blankCard.Kind == FitsValueKind.Integer ? blankCard.Integer : null;
        return new FitsGroups(raw, shape.Bitpix, parameters, dataLength, checked((int)shape.Gcount), bscale, bzero, blank, scales, zeros, names, mode);
    }

    private double ReadAt(int index) => Pixels.Read(_raw.Span, _bitpix, index);
}
