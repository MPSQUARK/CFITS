using System.Buffers.Binary;
using System.IO.Compression;

namespace CFITS;

public sealed class FitsDocumentBuilder
{
    private readonly List<Action<Stream>> _writers = [];
    private bool _hasPrimary;
    private bool _checksum;

    public static FitsDocumentBuilder Create() => new();

    public FitsDocumentBuilder Checksum()
    {
        _checksum = true;
        return this;
    }

    public void Write(Stream stream)
    {
        if (_checksum)
        {
            using var document = Build();
            document.Write(stream);
            return;
        }

        foreach (var write in _writers)
        {
            write(stream);
        }
    }

    public FitsDocumentBuilder Image(ReadOnlySpan<int> axes, ReadOnlySpan<byte> values, Action<FitsHeaderBuilder>? header = null) =>
        AddImage(8, axes, values, static (destination, value) => destination[0] = value, header);

    public FitsDocumentBuilder Image(ReadOnlySpan<int> axes, ReadOnlySpan<short> values, Action<FitsHeaderBuilder>? header = null) =>
        AddImage(16, axes, values, static (destination, value) => BinaryPrimitives.WriteInt16BigEndian(destination, value), header);

    public FitsDocumentBuilder Image(ReadOnlySpan<int> axes, ReadOnlySpan<int> values, Action<FitsHeaderBuilder>? header = null) =>
        AddImage(32, axes, values, static (destination, value) => BinaryPrimitives.WriteInt32BigEndian(destination, value), header);

    public FitsDocumentBuilder Image(ReadOnlySpan<int> axes, ReadOnlySpan<long> values, Action<FitsHeaderBuilder>? header = null) =>
        AddImage(64, axes, values, static (destination, value) => BinaryPrimitives.WriteInt64BigEndian(destination, value), header);

    public FitsDocumentBuilder Image(ReadOnlySpan<int> axes, ReadOnlySpan<float> values, Action<FitsHeaderBuilder>? header = null) =>
        AddImage(-32, axes, values, static (destination, value) => BinaryPrimitives.WriteSingleBigEndian(destination, value), header);

    public FitsDocumentBuilder Image(ReadOnlySpan<int> axes, ReadOnlySpan<double> values, Action<FitsHeaderBuilder>? header = null) =>
        AddImage(-64, axes, values, static (destination, value) => BinaryPrimitives.WriteDoubleBigEndian(destination, value), header);

    public FitsDocumentBuilder GzipImage(ReadOnlySpan<int> axes, ReadOnlySpan<short> values) =>
        AddGzipImage(16, axes, values, static (destination, value) => BinaryPrimitives.WriteInt16BigEndian(destination, value), shuffle: false);

    public FitsDocumentBuilder GzipImage(ReadOnlySpan<int> axes, ReadOnlySpan<float> values, bool shuffle = false) =>
        AddGzipImage(-32, axes, values, static (destination, value) => BinaryPrimitives.WriteSingleBigEndian(destination, value), shuffle);

    public FitsDocumentBuilder AsciiTable(AsciiTableBuilder table, Action<FitsHeaderBuilder>? header = null)
    {
        EnsurePrimary();
        var payload = table.Encode();
        _writers.Add(stream => WriteHdu(stream, payload.Header, payload.Data, header, extension: true));
        return this;
    }

    public FitsDocumentBuilder BinaryTable(BinaryTableBuilder table, Action<FitsHeaderBuilder>? header = null)
    {
        EnsurePrimary();
        var payload = table.Encode();
        _writers.Add(stream => WriteHdu(stream, payload.Header, payload.Data, header, extension: true));
        return this;
    }

    public FitsDocumentBuilder RandomGroups(int bitpix, ReadOnlySpan<int> dataAxes, int parameterCount, ReadOnlySpan<double> parameters, ReadOnlySpan<double> data, IReadOnlyList<string>? parameterNames = null, Action<FitsHeaderBuilder>? header = null)
    {
        if (_hasPrimary)
        {
            throw new FitsException("Random groups occupy the primary HDU.", new FitsLocation());
        }

        int dataPerGroup = Product(dataAxes);
        if (parameterCount < 0 || (parameterCount > 0 && parameters.Length % parameterCount != 0))
        {
            throw new FitsException("Group parameters do not fill a whole number of groups.", new FitsLocation());
        }

        int groups = parameterCount == 0 ? 0 : parameters.Length / parameterCount;
        if (parameterCount == 0 && dataPerGroup > 0)
        {
            groups = data.Length / dataPerGroup;
        }
        if (data.Length != groups * dataPerGroup)
        {
            throw new FitsException("Group data does not match the axes and group count.", new FitsLocation());
        }

        var axes = dataAxes.ToArray();
        var parameterCopy = parameters.ToArray();
        var dataCopy = data.ToArray();
        var names = parameterNames?.ToArray();
        _writers.Add(stream => WriteGroups(stream, bitpix, axes, parameterCount, groups, parameterCopy, dataCopy, names, header));
        _hasPrimary = true;
        return this;
    }

    public FitsDocument Build()
    {
        using var stream = new MemoryStream();
        foreach (var write in _writers)
        {
            if (!_checksum)
            {
                write(stream);
                continue;
            }

            using var hdu = new MemoryStream();
            write(hdu);
            byte[] stamped = FitsChecksum.Stamp(hdu.ToArray());
            stream.Write(stamped);
        }

        return FitsDocument.Parse(stream.ToArray());
    }

    private void EnsurePrimary()
    {
        if (_hasPrimary)
        {
            return;
        }

        Image(ReadOnlySpan<int>.Empty, ReadOnlySpan<byte>.Empty, static header => header.Logical("EXTEND", true));
    }

    private FitsDocumentBuilder AddImage<T>(int bitpix, ReadOnlySpan<int> axes, ReadOnlySpan<T> values, Encode<T> encode, Action<FitsHeaderBuilder>? header)
    {
        int count = Product(axes);
        if (values.Length != count)
        {
            throw new FitsException("The image value count does not match the axes.", new FitsLocation());
        }

        int width = Math.Abs(bitpix) / 8;
        var data = new byte[count * width];
        for (int index = 0; index < count; index++)
        {
            encode(data.AsSpan(index * width, width), values[index]);
        }

        bool extension = _hasPrimary;
        var capturedAxes = axes.ToArray();
        var capturedHeader = header;
        _writers.Add(stream =>
        {
            var cards = FitsHeader.Builder();
            if (extension)
            {
                cards.Text("XTENSION", "IMAGE   ");
            }
            else
            {
                cards.Logical("SIMPLE", true);
            }

            cards.Integer("BITPIX", bitpix);
            cards.Integer("NAXIS", capturedAxes.Length);
            for (int axis = 0; axis < capturedAxes.Length; axis++)
            {
                cards.Integer($"NAXIS{axis + 1}", capturedAxes[axis]);
            }

            if (extension)
            {
                cards.Integer("PCOUNT", 0);
                cards.Integer("GCOUNT", 1);
            }

            capturedHeader?.Invoke(cards);
            WriteHdu(stream, cards.Build(), data, extra: null, extension);
        });
        _hasPrimary = true;
        return this;
    }

    private FitsDocumentBuilder AddGzipImage<T>(int bitpix, ReadOnlySpan<int> axes, ReadOnlySpan<T> values, Encode<T> encode, bool shuffle)
    {
        EnsurePrimary();
        int count = Product(axes);
        if (values.Length != count)
        {
            throw new FitsException("The image value count does not match the axes.", new FitsLocation());
        }

        int width = Math.Abs(bitpix) / 8;
        var raw = new byte[count * width];
        for (int index = 0; index < count; index++)
        {
            encode(raw.AsSpan(index * width, width), values[index]);
        }

        if (shuffle)
        {
            raw = Shuffle(raw, width);
        }

        byte[] compressed = Gzip(raw);
        var capturedAxes = axes.ToArray();
        _writers.Add(stream =>
        {
            var cards = FitsHeader.Builder();
            cards.Text("XTENSION", "BINTABLE");
            cards.Integer("BITPIX", 8);
            cards.Integer("NAXIS", 2);
            cards.Integer("NAXIS1", 16);
            cards.Integer("NAXIS2", 1);
            cards.Integer("PCOUNT", compressed.Length);
            cards.Integer("GCOUNT", 1);
            cards.Integer("TFIELDS", 1);
            cards.Text("TTYPE1", "COMPRESSED_DATA");
            cards.Text("TFORM1", "1QB");
            cards.Integer("THEAP", 16);
            cards.Logical("ZIMAGE", true);
            cards.Text("ZCMPTYPE", shuffle ? "GZIP_2" : "GZIP_1");
            cards.Integer("ZBITPIX", bitpix);
            cards.Integer("ZNAXIS", capturedAxes.Length);
            cards.Text("ZNAME1", "BYTEPIX");
            cards.Integer("ZVAL1", width);
            for (int axis = 0; axis < capturedAxes.Length; axis++)
            {
                cards.Integer($"ZNAXIS{axis + 1}", capturedAxes[axis]);
                cards.Integer($"ZTILE{axis + 1}", capturedAxes[axis]);
            }

            var data = new byte[16 + compressed.Length];
            BinaryPrimitives.WriteInt64BigEndian(data, compressed.Length);
            BinaryPrimitives.WriteInt64BigEndian(data.AsSpan(8), 0);
            compressed.CopyTo(data.AsSpan(16));
            WriteHdu(stream, cards.Build(), data, extra: null, extension: true);
        });
        return this;
    }

    private static byte[] Gzip(byte[] raw)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(raw);
        }

        return output.ToArray();
    }

    private static byte[] Shuffle(byte[] source, int width)
    {
        if (width <= 1)
        {
            return source;
        }

        int count = source.Length / width;
        var bytes = new byte[source.Length];
        for (int item = 0; item < count; item++)
        {
            for (int byteIndex = 0; byteIndex < width; byteIndex++)
            {
                bytes[(byteIndex * count) + item] = source[(item * width) + byteIndex];
            }
        }

        return bytes;
    }

    private static void WriteHdu(Stream stream, FitsHeader header, byte[] data, Action<FitsHeaderBuilder>? extra, bool extension)
    {
        if (extra is not null)
        {
            var editor = FitsHeader.Builder();
            Copy(header, editor);
            extra(editor);
            header = editor.Build();
        }

        header.Write(stream);
        stream.Write(data);
        int pad = (int)(FitsBlock.Pad(data.Length) - data.Length);
        if (pad > 0)
        {
            stream.Write(new byte[pad]);
        }
    }

    private static void Copy(FitsHeader source, FitsHeaderBuilder destination)
    {
        foreach (var card in source.Cards)
        {
            switch (card.Kind)
            {
                case FitsValueKind.Logical:
                    destination.Logical(card.Keyword, card.Logical, card.Comment);
                    break;
                case FitsValueKind.Integer:
                    destination.Integer(card.Keyword, card.Integer, card.Comment);
                    break;
                case FitsValueKind.Real:
                    destination.Real(card.Keyword, card.Real, card.Comment);
                    break;
                case FitsValueKind.String:
                    destination.Text(card.Keyword, card.Text ?? "", card.Comment);
                    break;
                case FitsValueKind.Commentary:
                    if (card.Keyword == "COMMENT")
                    {
                        destination.Comment(card.Text ?? "");
                    }
                    else if (card.Keyword == "HISTORY")
                    {
                        destination.History(card.Text ?? "");
                    }
                    else
                    {
                        destination.Blank(card.Text ?? "");
                    }

                    break;
            }
        }
    }

    private static void WriteGroups(Stream stream, int bitpix, int[] dataAxes, int parameterCount, int groups, double[] parameters, double[] data, string[]? names, Action<FitsHeaderBuilder>? extra)
    {
        int dataPerGroup = 1;
        foreach (int axis in dataAxes)
        {
            dataPerGroup *= axis;
        }

        if (dataAxes.Length == 0)
        {
            dataPerGroup = 0;
        }

        int valuesPerGroup = parameterCount + dataPerGroup;
        int width = Math.Abs(bitpix) / 8;
        var payload = new byte[groups * valuesPerGroup * width];
        for (int group = 0; group < groups; group++)
        {
            for (int parameter = 0; parameter < parameterCount; parameter++)
            {
                Pixels.Write(payload, bitpix, (group * valuesPerGroup) + parameter, parameters[(group * parameterCount) + parameter]);
            }

            for (int element = 0; element < dataPerGroup; element++)
            {
                Pixels.Write(payload, bitpix, (group * valuesPerGroup) + parameterCount + element, data[(group * dataPerGroup) + element]);
            }
        }

        var header = FitsHeader.Builder()
            .Logical("SIMPLE", true)
            .Integer("BITPIX", bitpix)
            .Integer("NAXIS", dataAxes.Length + 1)
            .Integer("NAXIS1", 0);
        for (int axis = 0; axis < dataAxes.Length; axis++)
        {
            header.Integer($"NAXIS{axis + 2}", dataAxes[axis]);
        }

        header.Logical("GROUPS", true).Integer("PCOUNT", parameterCount).Integer("GCOUNT", groups);
        for (int parameter = 0; parameter < parameterCount; parameter++)
        {
            header.Text($"PTYPE{parameter + 1}", names is not null && parameter < names.Length ? names[parameter] : $"P{parameter + 1}");
        }

        extra?.Invoke(header);
        header.Build().Write(stream);
        stream.Write(payload);
        int pad = (int)(FitsBlock.Pad(payload.Length) - payload.Length);
        if (pad > 0)
        {
            stream.Write(new byte[pad]);
        }
    }

    private static int Product(ReadOnlySpan<int> axes)
    {
        if (axes.Length == 0)
        {
            return 0;
        }

        int product = 1;
        foreach (int axis in axes)
        {
            product = checked(product * axis);
        }

        return product;
    }

    private delegate void Encode<T>(Span<byte> destination, T value);
}
