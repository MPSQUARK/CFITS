namespace CFITS;

public sealed partial class FitsImage
{
    /// <summary>
    /// Reads the axes of one image HDU and leaves the stream where it started.
    /// </summary>
    public static FitsImageLayout Describe(Stream stream, int hduIndex = 0) =>
        Describe(stream, hduIndex, extensionName: null, options: null);

    /// <summary>
    /// Reads the axes of one image HDU and leaves the stream where it started.
    /// </summary>
    public static FitsImageLayout Describe(Stream stream, int hduIndex, string? extensionName, FitsOpenOptions? options = null) =>
        FitsDirectImage.Describe(stream, hduIndex, extensionName, options ?? new FitsOpenOptions());

    /// <summary>
    /// BOYB (bring your own buffer): decodes one image HDU into <paramref name="destination"/> without allocating a final pixel array.
    /// NAXIS1 is the fastest index, so a 2D image occupies <c>destination[y * naxis1 + x]</c>.
    /// </summary>
    public static FitsImageLayout Read<T>(Stream stream, Span<T> destination, int hduIndex = 0)
        where T : unmanaged =>
        Read(stream, destination, hduIndex, extensionName: null, options: null);

    /// <summary>
    /// BOYB (bring your own buffer): decodes one image HDU into <paramref name="destination"/> without allocating a final pixel array.
    /// NAXIS1 is the fastest index, so a 2D image occupies <c>destination[y * naxis1 + x]</c>.
    /// </summary>
    public static FitsImageLayout Read<T>(Stream stream, Span<T> destination, int hduIndex, string? extensionName, FitsOpenOptions? options = null)
        where T : unmanaged =>
        FitsDirectImage.Read(stream, destination, hduIndex, extensionName, options ?? new FitsOpenOptions());
}
