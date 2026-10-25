namespace CFITS;

public abstract class FitsTypeInfo<T>
{
    public abstract T Read(Stream stream);

    public virtual T Read(Stream stream, FitsOpenOptions options)
    {
        if (options.ScaleMode != FitsScaleMode.Physical || options.VerifyChecksum)
        {
            throw new NotSupportedException("This serializer does not support non-default FITS open options.");
        }

        return Read(stream);
    }

    public abstract void Write(Stream stream, T value);

    public virtual void Write(FitsDocumentBuilder builder, T value)
    {
        ArgumentNullException.ThrowIfNull(builder);
        throw new NotSupportedException("This serializer does not support writing into an existing FITS document builder.");
    }
}
