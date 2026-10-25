namespace CFITS;

public ref struct FitsReader
{
    private readonly ReadOnlySpan<byte> _source;
    private int _offset;
    private int _hduStart;
    private int _hduIndex;
    private State _state;
    private long _dataOffset;
    private long _dataLength;
    private HduShape _shape;
    private FitsCardView _card;

    public FitsReader(ReadOnlySpan<byte> source)
    {
        _source = source;
        _hduIndex = -1;
        _state = State.Between;
    }

    public FitsToken TokenType { get; private set; }

    public readonly int HduIndex => _hduIndex;

    public readonly int HeaderOffset => _hduStart;

    public readonly FitsCardView Card => _card;

    public readonly long DataOffset => _dataOffset;

    public readonly long DataLength => _dataLength;

    internal readonly HduShape Shape => _shape;

    public long SpecialOffset { get; private set; }

    public int SpecialLength { get; private set; }

    public bool Read()
    {
        switch (_state)
        {
            case State.Between:
                return StartHdu();
            case State.Header:
                return ReadCard();
            case State.Data:
                SkipData();
                return StartHdu();
            default:
                return false;
        }
    }

    private bool StartHdu()
    {
        if (_offset >= _source.Length)
        {
            TokenType = FitsToken.None;
            _state = State.Done;
            return false;
        }

        if (_source.Length - _offset < FitsBlock.CardSize)
        {
            throw new FitsException("The file ends in the middle of a header record.", new FitsLocation(Position: _offset));
        }

        var keyword = TrimEnd(_source.Slice(_offset, 8));
        bool primary = _hduIndex < 0;
        if (primary)
        {
            if (!keyword.SequenceEqual("SIMPLE"u8))
            {
                throw new FitsException("A FITS file must start with SIMPLE.", new FitsLocation(Position: _offset));
            }
        }
        else if (!keyword.SequenceEqual("XTENSION"u8))
        {
            int remaining = _source.Length - _offset;
            if (remaining % FitsBlock.Size != 0)
            {
                throw new FitsException("Trailing bytes are not a whole number of 2880-byte records.", new FitsLocation(Position: _offset));
            }

            SpecialOffset = _offset;
            SpecialLength = remaining;
            TokenType = FitsToken.SpecialRecords;
            _offset = _source.Length;
            _state = State.Done;
            return true;
        }

        _hduIndex++;
        _hduStart = _offset;
        TokenType = FitsToken.Hdu;
        _state = State.Header;
        return true;
    }

    private bool ReadCard()
    {
        if (_source.Length - _offset < FitsBlock.CardSize)
        {
            throw new FitsException("The header ended before the END record.", new FitsLocation(_hduIndex, Position: _offset));
        }

        var view = FitsCardView.Parse(_source.Slice(_offset, FitsBlock.CardSize), _offset);
        int cardAt = _offset;
        _offset += FitsBlock.CardSize;
        if (view.IsEnd)
        {
            _dataOffset = (cardAt / FitsBlock.Size + 1L) * FitsBlock.Size;
            if (_dataOffset > _source.Length)
            {
                _dataOffset = _source.Length;
            }

            var header = _source.Slice(_hduStart, (int)Math.Min(_dataOffset, _source.Length) - _hduStart);
            _shape = DataLayout.Measure(header, _hduIndex);
            _dataLength = _shape.DataBytes;
            long available = _source.Length - _dataOffset;
            if (available < _dataLength)
            {
                throw new FitsException(
                    "The file ends before the data array is complete.",
                    new FitsLocation(_hduIndex, Position: _dataOffset));
            }

            TokenType = FitsToken.EndHeader;
            _state = State.Data;
            return true;
        }

        _card = view;
        TokenType = FitsToken.Card;
        return true;
    }

    private void SkipData()
    {
        if (_dataOffset > _source.Length)
        {
            throw new FitsException("The data block starts past the end of the file.", new FitsLocation(_hduIndex, Position: _dataOffset));
        }

        long available = _source.Length - _dataOffset;
        if (available < _dataLength)
        {
            throw new FitsException("The file ends before the data array is complete.", new FitsLocation(_hduIndex, Position: _dataOffset));
        }

        long padded = FitsBlock.Pad(_dataLength);
        _offset = (int)(_dataOffset + Math.Min(padded, available));
        _state = State.Between;
    }

    private static ReadOnlySpan<byte> TrimEnd(ReadOnlySpan<byte> value)
    {
        int end = value.Length;
        while (end > 0 && value[end - 1] == (byte)' ')
        {
            end--;
        }

        return value[..end];
    }

    private enum State
    {
        Between,
        Header,
        Data,
        Done,
    }
}
