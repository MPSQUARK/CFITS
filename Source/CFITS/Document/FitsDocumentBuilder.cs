using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CFITS;

public sealed class FitsDocumentBuilder
{
    private static readonly byte[] Padding = new byte[FitsBlock.Size];
    private readonly List<Action<Stream>> _writers = [];
    private bool _hasPrimary;
    private bool _checksum;
    private byte[]? _specialRecords;

    public static FitsDocumentBuilder Create() => new();

    public FitsDocumentBuilder Checksum()
    {
        _checksum = true;
        return this;
    }

    /// <summary>Ensures that subsequent HDUs are emitted as extensions.</summary>
    public FitsDocumentBuilder EnsurePrimaryHdu()
    {
        EnsurePrimary();
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

        WriteUnchecked(stream);
    }

    private void WriteUnchecked(Stream stream)
    {
        foreach (var write in _writers)
        {
            write(stream);
        }

        if (_specialRecords is not null)
        {
            stream.Write(_specialRecords);
        }
    }

    public FitsDocumentBuilder Image(ReadOnlySpan<int> axes, ReadOnlySpan<byte> values, Action<FitsHeaderBuilder>? header = null) =>
        AddImage(8, axes, values, header);

    public FitsDocumentBuilder Image(ReadOnlySpan<int> axes, ReadOnlySpan<short> values, Action<FitsHeaderBuilder>? header = null) =>
        AddImage(16, axes, values, header);

    public FitsDocumentBuilder Image(ReadOnlySpan<int> axes, ReadOnlySpan<int> values, Action<FitsHeaderBuilder>? header = null) =>
        AddImage(32, axes, values, header);

    public FitsDocumentBuilder Image(ReadOnlySpan<int> axes, ReadOnlySpan<long> values, Action<FitsHeaderBuilder>? header = null) =>
        AddImage(64, axes, values, header);

    public FitsDocumentBuilder Image(ReadOnlySpan<int> axes, ReadOnlySpan<float> values, Action<FitsHeaderBuilder>? header = null) =>
        AddImage(-32, axes, values, header);

    public FitsDocumentBuilder Image(ReadOnlySpan<int> axes, ReadOnlySpan<double> values, Action<FitsHeaderBuilder>? header = null) =>
        AddImage(-64, axes, values, header);

    public FitsDocumentBuilder GzipImage(ReadOnlySpan<int> axes, ReadOnlySpan<short> values) =>
        AddGzipImage(16, axes, values, shuffle: false);

    public FitsDocumentBuilder GzipImage(ReadOnlySpan<int> axes, ReadOnlySpan<float> values, bool shuffle = false) =>
        AddGzipImage(-32, axes, values, shuffle);

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

    internal FitsDocument BinaryOrAsciiRawTable(FitsHeader tableHeader, byte[] tableData, bool ascii)
    {
        EnsurePrimary();
        _writers.Add(stream => WriteHdu(stream, tableHeader, tableData, extra: null, extension: true));
        return Build();
    }

    /// <summary>Adds an already encoded HDU without changing its header or payload.</summary>
    public FitsDocumentBuilder Hdu(FitsHduSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (_hasPrimary && snapshot.Index == 0)
        {
            throw new FitsException("Only one primary HDU is allowed.", new FitsLocation(snapshot.Index));
        }

        if (!_hasPrimary)
        {
            if (snapshot.Index != 0)
            {
                throw new FitsException("The first HDU must be the primary HDU.", new FitsLocation(snapshot.Index));
            }

            _hasPrimary = true;
        }

        _writers.Add(snapshot.Write);
        return this;
    }

    /// <summary>Appends FITS records that follow the final HDU.</summary>
    public FitsDocumentBuilder SpecialRecords(ReadOnlyMemory<byte> records)
    {
        _specialRecords = records.ToArray();
        return this;
    }

    public FitsDocumentBuilder RandomGroups(int bitpix, ReadOnlySpan<int> dataAxes, int parameterCount, ReadOnlySpan<double> parameters, ReadOnlySpan<double> data, IReadOnlyList<string>? parameterNames = null, Action<FitsHeaderBuilder>? header = null)
    {
        if (_hasPrimary)
        {
            throw new FitsException("Random groups occupy the primary HDU.", new FitsLocation());
        }

        if (bitpix is not (8 or 16 or 32 or 64 or -32 or -64))
        {
            throw new FitsException($"BITPIX {bitpix} is not a legal FITS value.", new FitsLocation(Keyword: "BITPIX"));
        }

        if (dataAxes.Length == 0)
        {
            throw new FitsException("Random groups require at least one positive data axis.", new FitsLocation());
        }

        foreach (int axis in dataAxes)
        {
            if (axis <= 0)
            {
                throw new FitsException("Random groups require positive data axes.", new FitsLocation());
            }
        }

        int dataPerGroup;
        try
        {
            dataPerGroup = Product(dataAxes);
        }
        catch (OverflowException error)
        {
            throw new FitsException("Random-group axes exceed the supported data size.", new FitsLocation(Keyword: "NAXIS"), error);
        }
        if (parameterCount < 0
            || (parameterCount == 0 && parameters.Length != 0)
            || (parameterCount > 0 && parameters.Length % parameterCount != 0))
        {
            throw new FitsException("Group parameters do not fill a whole number of groups.", new FitsLocation());
        }

        if (parameterNames is not null && parameterNames.Count > parameterCount)
        {
            throw new FitsException("Random-group parameter names exceed the parameter count.", new FitsLocation());
        }

        int groups = parameterCount == 0 ? 0 : parameters.Length / parameterCount;
        if (parameterCount == 0 && dataPerGroup > 0)
        {
            groups = data.Length / dataPerGroup;
        }
        long expectedData = checked((long)groups * dataPerGroup);
        if (data.Length != expectedData)
        {
            throw new FitsException("Group data does not match the axes and group count.", new FitsLocation());
        }
        try
        {
            _ = checked((long)groups * ((long)parameterCount + dataPerGroup) * (Math.Abs(bitpix) / 8));
        }
        catch (OverflowException error)
        {
            throw new FitsException("Random-group data exceeds the supported data size.", new FitsLocation(), error);
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
        if (_checksum)
        {
            using var stream = new MemoryStream();
            foreach (var write in _writers)
            {
                using var hdu = new MemoryStream();
                write(hdu);
                byte[] stamped = FitsChecksum.Stamp(hdu.ToArray());
                stream.Write(stamped);
            }

            if (_specialRecords is not null)
            {
                stream.Write(_specialRecords);
            }

            return FitsDocument.ParseOwned(stream.ToArray());
        }

        var measured = new LengthStream();
        WriteUnchecked(measured);
        if (measured.Length > int.MaxValue)
        {
            throw new FitsException("The FITS file exceeds the supported data size.", new FitsLocation());
        }

        var owned = new byte[measured.Length];
        var buffer = new BufferStream(owned);
        WriteUnchecked(buffer);
        if (buffer.Written != owned.Length)
        {
            throw new FitsException("The FITS file length did not match the measured length.", new FitsLocation());
        }

        return FitsDocument.ParseOwned(owned);
    }

    private void EnsurePrimary()
    {
        if (_hasPrimary)
        {
            return;
        }

        Image(ReadOnlySpan<int>.Empty, ReadOnlySpan<byte>.Empty, static header => header.Logical("EXTEND", true));
    }

    private FitsDocumentBuilder AddImage<T>(int bitpix, ReadOnlySpan<int> axes, ReadOnlySpan<T> values, Action<FitsHeaderBuilder>? header)
        where T : unmanaged
    {
        int count = Product(axes);
        if (values.Length != count)
        {
            throw new FitsException("The image value count does not match the axes.", new FitsLocation());
        }

        int width = Math.Abs(bitpix) / 8;
        var retained = new T[values.Length];
        values.CopyTo(retained);

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
            if (bitpix < 0)
            {
                cards.Remove("BLANK");
            }

            cards.Build().Write(stream);
            WritePixels(stream, retained);
            int encoded = retained.Length * width;
            int pad = (int)(FitsBlock.Pad(encoded) - encoded);
            if (pad > 0)
            {
                stream.Write(Padding.AsSpan(0, pad));
            }
        });
        _hasPrimary = true;
        return this;
    }

    private FitsDocumentBuilder AddGzipImage<T>(int bitpix, ReadOnlySpan<int> axes, ReadOnlySpan<T> values, bool shuffle)
        where T : unmanaged
    {
        EnsurePrimary();
        int count = Product(axes);
        if (values.Length != count)
        {
            throw new FitsException("The image value count does not match the axes.", new FitsLocation());
        }

        int width = Math.Abs(bitpix) / 8;
        var raw = new byte[count * width];
        Pixels.WriteBigEndian(values, raw);

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
            stream.Write(Padding.AsSpan(0, pad));
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

        int valuesPerGroup = checked(parameterCount + dataPerGroup);
        int width = Math.Abs(bitpix) / 8;
        var payload = new byte[checked(groups * valuesPerGroup * width)];
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
            stream.Write(Padding.AsSpan(0, pad));
        }
    }

    private static void WritePixels<T>(Stream stream, ReadOnlySpan<T> values)
        where T : unmanaged
    {
        if (typeof(T) == typeof(byte))
        {
            stream.Write(MemoryMarshal.AsBytes(values));
            return;
        }

        int width = Unsafe.SizeOf<T>();
        byte[] rented = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            int capacity = rented.Length / width * width;
            for (int index = 0; index < values.Length;)
            {
                int count = Math.Min(values.Length - index, capacity / width);
                Span<byte> destination = rented.AsSpan(0, count * width);
                if (typeof(T) == typeof(short))
                {
                    FitsEndian.WriteInt16(MemoryMarshal.Cast<T, short>(values.Slice(index, count)), destination);
                }
                else if (typeof(T) == typeof(int))
                {
                    FitsEndian.WriteInt32(MemoryMarshal.Cast<T, int>(values.Slice(index, count)), destination);
                }
                else if (typeof(T) == typeof(long))
                {
                    FitsEndian.WriteInt64(MemoryMarshal.Cast<T, long>(values.Slice(index, count)), destination);
                }
                else if (typeof(T) == typeof(float))
                {
                    FitsEndian.WriteSingle(MemoryMarshal.Cast<T, float>(values.Slice(index, count)), destination);
                }
                else if (typeof(T) == typeof(double))
                {
                    FitsEndian.WriteDouble(MemoryMarshal.Cast<T, double>(values.Slice(index, count)), destination);
                }
                else
                {
                    throw new FitsException("The image element type is not supported.", new FitsLocation());
                }

                stream.Write(destination);
                index += count;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private sealed class LengthStream : Stream
    {
        private long _length;

        public override long Length => _length;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Position { get => _length; set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => _length += count;

        public override void Write(ReadOnlySpan<byte> buffer) => _length += buffer.Length;
    }

    private sealed class BufferStream(byte[] buffer) : Stream
    {
        private int _written;

        public int Written => _written;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _written;
        public override long Position { get => _written; set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] destination, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] destination, int offset, int count) =>
            Write(destination.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> destination)
        {
            destination.CopyTo(buffer.AsSpan(_written));
            _written += destination.Length;
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

}
