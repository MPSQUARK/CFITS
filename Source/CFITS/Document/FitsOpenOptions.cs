namespace CFITS;

public sealed class FitsOpenOptions
{
    public FitsScaleMode ScaleMode { get; init; } = FitsScaleMode.Physical;

    public bool VerifyChecksum { get; init; }
}
