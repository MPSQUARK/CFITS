namespace CFITS.Tests;

public sealed class WcsValidationConventionTests
{
    [Theory]
    [InlineData("CRPIX1", double.NaN)]
    [InlineData("CRVAL1", double.PositiveInfinity)]
    [InlineData("PC1_1", double.NegativeInfinity)]
    public void RejectsNonfiniteWcsValues(string keyword, double value)
    {
        var builder = FitsHeader.Builder()
            .Integer("WCSAXES", 1)
            .Real("CRPIX1", keyword == "CRPIX1" ? value : 1)
            .Real("CRVAL1", keyword == "CRVAL1" ? value : 0)
            .Real("CDELT1", 1)
            .Real("PC1_1", keyword == "PC1_1" ? value : 1);

        var error = Assert.Throws<FitsException>(() => FitsWorld.FromHeader(builder.Build()));

        Assert.Contains(keyword, error.Message);
    }

    [Fact]
    public void RejectsCelestialLongitudeWithoutLatitude()
    {
        var error = Assert.Throws<FitsException>(() => FitsWorld.FromHeader(FitsHeader.Builder()
            .Integer("WCSAXES", 1)
            .Real("CRPIX1", 1)
            .Real("CRVAL1", 30)
            .Real("CDELT1", 1)
            .Text("CTYPE1", "RA---TAN")
            .Build()));

        Assert.Contains("celestial", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsCelestialLatitudeWithoutLongitude()
    {
        var error = Assert.Throws<FitsException>(() => FitsWorld.FromHeader(FitsHeader.Builder()
            .Integer("WCSAXES", 1)
            .Real("CRPIX1", 1)
            .Real("CRVAL1", 40)
            .Real("CDELT1", 1)
            .Text("CTYPE1", "DEC--TAN")
            .Build()));

        Assert.Contains("celestial", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
