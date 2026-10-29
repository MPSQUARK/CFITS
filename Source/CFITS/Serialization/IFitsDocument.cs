namespace CFITS;

public interface IFitsDocument<T>
    where T : IFitsDocument<T>
{
    static abstract T Read(Stream stream, FitsOpenOptions options);

    static abstract void Write(Stream stream, T value);

    static abstract void Write(FitsDocumentBuilder builder, T value);
}
