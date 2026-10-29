namespace CFITS;

/// <summary>
/// Forwards to <see cref="FitsParser"/>. Prefer <see cref="FitsParser"/> for new code.
/// </summary>
public static class FitsSerializer
{
    public static T Read<T>(Stream stream)
        where T : IFitsDocument<T> =>
        FitsParser.Deserialize<T>(stream);

    public static T Read<T>(Stream stream, FitsOpenOptions options)
        where T : IFitsDocument<T> =>
        FitsParser.Deserialize<T>(stream, options);

    public static T Read<T>(string path)
        where T : IFitsDocument<T> =>
        FitsParser.Deserialize<T>(path);

    public static T Read<T>(string path, FitsOpenOptions options)
        where T : IFitsDocument<T> =>
        FitsParser.Deserialize<T>(path, options);

    public static void Write<T>(Stream stream, T value)
        where T : IFitsDocument<T> =>
        FitsParser.Serialize(stream, value);

    public static void Write<T>(FitsDocumentBuilder builder, T value)
        where T : IFitsDocument<T> =>
        FitsParser.Serialize(builder, value);

    public static void Write<T>(string path, T value)
        where T : IFitsDocument<T> =>
        FitsParser.Serialize(path, value);
}
