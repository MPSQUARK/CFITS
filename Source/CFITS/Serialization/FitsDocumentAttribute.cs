namespace CFITS;

/// <summary>
/// Marks a partial type as a FITS document.
/// Unmapped HDUs and header cards are ignored unless the matching property is set to <see langword="false"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class FitsDocumentAttribute : Attribute
{
    /// <summary>
    /// When <see langword="true"/> (the default), HDUs that are not modeled are skipped.
    /// Set this to <see langword="false"/> and declare <c>[FitsOtherHdus]</c> to keep them.
    /// </summary>
    public bool IgnoreOtherHdus { get; init; } = true;

    /// <summary>
    /// When <see langword="true"/> (the default), cards that are not named on a keyword object are not retained.
    /// Set this to <see langword="false"/> to generate <c>PrimaryHeader</c> and extension <c>Header</c> properties.
    /// </summary>
    public bool IgnoreUnmappedHeaders { get; init; } = true;
}
