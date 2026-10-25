namespace CFITS;

public abstract class FitsDocumentTypeInfo<T> : FitsTypeInfo<T>
{
    public sealed override T Read(Stream stream)
    {
        using var document = FitsDocument.Open(stream);
        return Read(document);
    }

    public sealed override T Read(Stream stream, FitsOpenOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        using var document = FitsDocument.Open(stream, options);
        return Read(document);
    }

    public abstract T Read(FitsDocument document);
}
