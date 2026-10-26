namespace CFITS;

/// <summary>
/// Describes the coordinate represented by one WCS axis.
/// </summary>
public sealed record WcsAxisMetadata(
    int Number,
    string Type,
    string Unit,
    bool IsCelestial,
    bool IsSpectral,
    bool IsTime,
    string? Projection,
    string? Algorithm,
    string Name,
    double? RandomError,
    double? SystematicError,
    string? ReferenceSystem = null,
    double? RestFrequency = null,
    double? RestWavelength = null,
    string? VelocitySystem = null,
    int? VelocityReference = null);
