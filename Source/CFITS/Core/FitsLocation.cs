namespace CFITS;

/// <summary>Where a format error was found.</summary>
public readonly record struct FitsLocation(
    int? HduIndex = null,
    string? Keyword = null,
    string? Column = null,
    long? Position = null);
