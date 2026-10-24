namespace CFITS;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class FitsSerializableAttribute : Attribute
{
    public FitsSerializableAttribute(Type type) => Type = type;

    public Type Type { get; }
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class FitsImageAttribute : Attribute
{
    public FitsImageAttribute()
    {
    }

    public FitsImageAttribute(string name) => Name = name;

    public string? Name { get; }
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class FitsTableAttribute : Attribute
{
    public FitsTableAttribute()
    {
    }

    public FitsTableAttribute(string name) => Name = name;

    public string? Name { get; }
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class FitsKeywordAttribute : Attribute
{
    public FitsKeywordAttribute(string name) => Name = name;

    public string Name { get; }
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class FitsColumnAttribute : Attribute
{
    public FitsColumnAttribute(string name) => Name = name;

    public string Name { get; }
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class FitsDataAttribute : Attribute;

public abstract class FitsSerializerContext;

public abstract class FitsTypeInfo<T>
{
    public abstract T Read(Stream stream);

    public abstract void Write(Stream stream, T value);
}

public abstract class FitsTableTypeInfo<T>
{
    public abstract IReadOnlyList<T> Read(Stream stream);

    public abstract void Write(Stream stream, IReadOnlyList<T> rows);
}

public static class FitsSerializer
{
    public static T Read<T>(Stream stream, FitsTypeInfo<T> type) => type.Read(stream);

    public static T Read<T>(string path, FitsTypeInfo<T> type)
    {
        using var stream = File.OpenRead(path);
        return type.Read(stream);
    }

    public static void Write<T>(Stream stream, T value, FitsTypeInfo<T> type) => type.Write(stream, value);

    public static void Write<T>(string path, T value, FitsTypeInfo<T> type)
    {
        using var stream = File.Create(path);
        type.Write(stream, value);
    }

    public static IReadOnlyList<T> Read<T>(Stream stream, FitsTableTypeInfo<T> type) => type.Read(stream);

    public static IReadOnlyList<T> Read<T>(string path, FitsTableTypeInfo<T> type)
    {
        using var stream = File.OpenRead(path);
        return type.Read(stream);
    }

    public static void Write<T>(Stream stream, IReadOnlyList<T> rows, FitsTableTypeInfo<T> type) => type.Write(stream, rows);

    public static void Write<T>(string path, IReadOnlyList<T> rows, FitsTableTypeInfo<T> type)
    {
        using var stream = File.Create(path);
        type.Write(stream, rows);
    }
}
