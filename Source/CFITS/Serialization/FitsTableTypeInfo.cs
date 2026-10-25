namespace CFITS;

public abstract class FitsTableTypeInfo<T>
{
    public abstract IReadOnlyList<T> Read(Stream stream);

    public virtual IReadOnlyList<T> Read(Stream stream, FitsOpenOptions options)
    {
        if (options.ScaleMode != FitsScaleMode.Physical || options.VerifyChecksum)
        {
            throw new NotSupportedException("This serializer does not support non-default FITS open options.");
        }

        return Read(stream);
    }

    public abstract void Write(Stream stream, IReadOnlyList<T> rows);

    public virtual void Write(FitsDocumentBuilder builder, IReadOnlyList<T> rows)
    {
        ArgumentNullException.ThrowIfNull(builder);
        throw new NotSupportedException("This serializer does not support writing into an existing FITS document builder.");
    }
}
