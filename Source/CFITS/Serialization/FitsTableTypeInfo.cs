namespace CFITS;

public abstract class FitsTableTypeInfo<T>
{
    public abstract IReadOnlyList<T> Read(Stream stream);

    public abstract void Write(Stream stream, IReadOnlyList<T> rows);
}
