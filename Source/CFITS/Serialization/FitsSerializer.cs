namespace CFITS;

public static class FitsSerializer
{
    public static T Read<T>(Stream stream, FitsTypeInfo<T> type) => type.Read(stream);

    public static T Read<T>(Stream stream, FitsTypeInfo<T> type, FitsOpenOptions options) =>
        type.Read(stream, options);

    public static T Read<T>(string path, FitsTypeInfo<T> type)
    {
        using var stream = File.OpenRead(path);
        return type.Read(stream);
    }

    public static T Read<T>(string path, FitsTypeInfo<T> type, FitsOpenOptions options)
    {
        using var stream = File.OpenRead(path);
        return type.Read(stream, options);
    }

    public static void Write<T>(Stream stream, T value, FitsTypeInfo<T> type) => type.Write(stream, value);

    public static void Write<T>(string path, T value, FitsTypeInfo<T> type)
    {
        using var stream = File.Create(path);
        type.Write(stream, value);
    }

    public static IReadOnlyList<T> Read<T>(Stream stream, FitsTableTypeInfo<T> type) => type.Read(stream);

    public static IReadOnlyList<T> Read<T>(Stream stream, FitsTableTypeInfo<T> type, FitsOpenOptions options) =>
        type.Read(stream, options);

    public static IReadOnlyList<T> Read<T>(string path, FitsTableTypeInfo<T> type)
    {
        using var stream = File.OpenRead(path);
        return type.Read(stream);
    }

    public static IReadOnlyList<T> Read<T>(string path, FitsTableTypeInfo<T> type, FitsOpenOptions options)
    {
        using var stream = File.OpenRead(path);
        return type.Read(stream, options);
    }

    public static void Write<T>(Stream stream, IReadOnlyList<T> rows, FitsTableTypeInfo<T> type) => type.Write(stream, rows);

    public static void Write<T>(string path, IReadOnlyList<T> rows, FitsTableTypeInfo<T> type)
    {
        using var stream = File.Create(path);
        type.Write(stream, rows);
    }
}
