using System.Buffers.Binary;
using CFITS.Data;

namespace CFITS;

/// <summary>Writes table rows supplied as their exact FITS wire representation.</summary>
public sealed class FitsRawTableBuilder
{
    private readonly bool _ascii;
    private readonly List<FitsTableColumnSchema> _columns = [];
    private readonly List<byte[]> _rows = [];
    private byte[] _heap = [];
    private long? _theap;

    public FitsRawTableBuilder(bool ascii) => _ascii = ascii;

    public FitsRawTableBuilder AddColumn(string name, string format, int? start = null, string? tdim = null) =>
        AddColumn(new FitsTableColumnSchema(name, format, start, tdim));

    public FitsRawTableBuilder AddColumn(FitsTableColumnSchema column)
    {
        ArgumentNullException.ThrowIfNull(column);
        _columns.Add(column);
        return this;
    }

    public FitsRawTableBuilder AddRow(ReadOnlyMemory<byte> row)
    {
        _rows.Add(row.ToArray());
        return this;
    }

    /// <summary>
    /// Adds a row without copying it. The caller must not mutate or reuse
    /// <paramref name="row"/> until <see cref="Build"/> has completed.
    /// </summary>
    public FitsRawTableBuilder AddRowOwned(byte[] row)
    {
        ArgumentNullException.ThrowIfNull(row);
        _rows.Add(row);
        return this;
    }

    public FitsRawTableBuilder SetHeap(ReadOnlyMemory<byte> heap, long? theap = null)
    {
        _heap = heap.ToArray();
        _theap = theap;
        return this;
    }

    /// <summary>
    /// Sets the heap without copying it. The caller must not mutate or reuse
    /// <paramref name="heap"/> until <see cref="Build"/> has completed.
    /// </summary>
    public FitsRawTableBuilder SetHeapOwned(byte[] heap, long? theap = null)
    {
        ArgumentNullException.ThrowIfNull(heap);
        _heap = heap;
        _theap = theap;
        return this;
    }

    public FitsDocument Build()
    {
        if (_columns.Count == 0)
            throw new FitsException("A raw table needs at least one column.", new FitsLocation());

        var specs = new ColumnSpec[_columns.Count];
        for (int index = 0; index < specs.Length; index++)
        {
            specs[index] = ColumnFormats.Parse(_columns[index].Format, _ascii);
        }

        int stride = GetStride(specs);
        for (int index = 0; index < _rows.Count; index++)
        {
            if (_rows[index].Length != stride)
            {
                throw new FitsException($"Raw table rows must contain exactly {stride} bytes.", new FitsLocation());
            }
        }
        long fixedRows = (long)stride * _rows.Count;
        long heapStart = _theap ?? fixedRows;
        if (_ascii && _heap.Length != 0)
            throw new FitsException("ASCII tables cannot contain heap bytes.", new FitsLocation());
        if (heapStart < fixedRows)
            throw new FitsException("THEAP cannot precede the fixed table rows.", new FitsLocation(Column: "THEAP"));
        long pcount = _ascii ? 0 : checked(heapStart - fixedRows + _heap.Length);

        var header = FitsHeader.Builder()
            .Text("XTENSION", _ascii ? "TABLE" : "BINTABLE")
            .Integer("BITPIX", 8)
            .Integer("NAXIS", 2)
            .Integer("NAXIS1", stride)
            .Integer("NAXIS2", _rows.Count)
            .Integer("PCOUNT", pcount)
            .Integer("GCOUNT", 1)
            .Integer("TFIELDS", _columns.Count);

        int previousEnd = 0;
        for (int index = 0; index < _columns.Count; index++)
        {
            var column = _columns[index];
            var spec = specs[index];
            header.Text($"TTYPE{index + 1}", column.Name);
            header.Text($"TFORM{index + 1}", column.Format);
            if (_ascii)
            {
                int start = column.Start ?? previousEnd + 1;
                if (start < 1 || start - 1 < previousEnd)
                    throw new FitsException($"ASCII column {index + 1} overlaps a previous column.", new FitsLocation(Column: $"TBCOL{index + 1}"));
                if (start - 1 + spec.Width > stride)
                    throw new FitsException($"ASCII column {index + 1} exceeds NAXIS1.", new FitsLocation(Column: $"TBCOL{index + 1}"));
                header.Integer($"TBCOL{index + 1}", start);
                previousEnd = start - 1 + spec.Width;
            }
            else if (column.Dimensions is not null)
            {
                header.Text($"TDIM{index + 1}", column.Dimensions);
            }
        }

        if (!_ascii && pcount > 0)
            header.Integer("THEAP", heapStart);
        byte[] data = new byte[checked(fixedRows + pcount)];
        for (int row = 0; row < _rows.Count; row++)
            _rows[row].CopyTo(data.AsSpan(row * stride));
        _heap.CopyTo(data.AsSpan(checked((int)heapStart)));

        return FitsDocumentBuilder.Create()
            .BinaryOrAsciiRawTable(header.Build(), data, _ascii);
    }

    private int GetStride(ColumnSpec[] specs)
    {
        if (_ascii)
        {
            int end = 0;
            for (int index = 0; index < _columns.Count; index++)
            {
                int start = _columns[index].Start ?? end + 1;
                end = Math.Max(end, checked(start - 1 + specs[index].Width));
            }

            return end;
        }

        return specs.Aggregate(0, (total, spec) => checked(total + spec.Width));
    }
}
