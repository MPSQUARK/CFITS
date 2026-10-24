namespace CFITS;

/// <summary>
/// CHECKSUM and DATASUM for one header-and-data unit.
/// A null match means that keyword was not in the header.
/// </summary>
public readonly record struct FitsIntegrity(bool? ChecksumMatches, bool? DataSumMatches, bool? UncompressedDataSumMatches, uint DataSum)
{
    public bool IsValid => ChecksumMatches != false && DataSumMatches != false && UncompressedDataSumMatches != false;
}
