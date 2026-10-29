namespace CFITS;

/// <summary>
/// Reads and writes a whole modeled FITS document.
/// Use <see cref="Deserialize{T}(Stream)"/> when every mapped member is needed.
/// Use the generated <c>Open</c> method on the document type to read one HDU at a time.
/// </summary>
public static class FitsParser
{
    public static T Deserialize<T>(Stream stream)
        where T : IFitsDocument<T> =>
        T.Read(stream, new FitsOpenOptions());

    public static T Deserialize<T>(Stream stream, FitsOpenOptions options)
        where T : IFitsDocument<T>
    {
        ArgumentNullException.ThrowIfNull(options);
        return T.Read(stream, options);
    }

    public static T Deserialize<T>(string path)
        where T : IFitsDocument<T>
    {
        using var stream = File.OpenRead(path);
        return T.Read(stream, new FitsOpenOptions());
    }

    public static T Deserialize<T>(string path, FitsOpenOptions options)
        where T : IFitsDocument<T>
    {
        ArgumentNullException.ThrowIfNull(options);
        using var stream = File.OpenRead(path);
        return T.Read(stream, options);
    }

    public static void Serialize<T>(Stream stream, T value)
        where T : IFitsDocument<T> =>
        T.Write(stream, value);

    public static void Serialize<T>(FitsDocumentBuilder builder, T value)
        where T : IFitsDocument<T> =>
        T.Write(builder, value);

    public static void Serialize<T>(string path, T value)
        where T : IFitsDocument<T>
    {
        using var stream = File.Create(path);
        T.Write(stream, value);
    }
}
