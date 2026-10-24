namespace CFITS;

public abstract class FitsTypeInfo<T>
{
    public abstract T Read(Stream stream);

    public abstract void Write(Stream stream, T value);
}
