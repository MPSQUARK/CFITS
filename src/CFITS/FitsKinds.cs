namespace CFITS;

public enum FitsValueKind
{
    Undefined,
    Logical,
    Integer,
    Real,
    Complex,
    String,
    Commentary,
    Raw,
}

public enum FitsToken
{
    None,
    Hdu,
    Card,
    EndHeader,
    SpecialRecords,
}

public enum FitsHduKind
{
    Image,
    AsciiTable,
    BinaryTable,
    RandomGroups,
    Unknown,
}

public enum FitsScaleMode
{
    Physical,
    Stored,
}

public sealed class FitsOpenOptions
{
    public FitsScaleMode ScaleMode { get; init; } = FitsScaleMode.Physical;

    public bool VerifyChecksum { get; init; }
}
