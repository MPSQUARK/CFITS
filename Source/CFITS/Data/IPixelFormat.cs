namespace CFITS.Data;

internal interface IPixelFormat
{
    int Bitpix { get; }

    int Width { get; }

    bool SupportsIntegerStorage { get; }

    double Read(ReadOnlySpan<byte> raw, int index);

    long ReadInteger(ReadOnlySpan<byte> raw, int index);

    void Write(Span<byte> destination, double stored);

    void Copy(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank, Span<double> destination);

    void Copy(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank, Span<float> destination);

    void Copy(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank, double[,] destination, int n1);

    void Copy(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank, double[,,] destination, int n1, int n2);

    void Copy(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank, float[,] destination, int n1);

    void Copy(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank, float[,,] destination, int n1, int n2);

    double Sum(ReadOnlySpan<byte> raw, int length, double scale, double zero, long? blank);
}
