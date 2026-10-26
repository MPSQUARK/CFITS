namespace CFITS.Tests;

public sealed class WcsExhaustiveComplianceTests
{
    [Fact]
    public void AlternateWcsFallsBackIndependentlyForEveryAxisCalibrationKeyword()
    {
        var header = FitsHeader.Builder()
            .Integer("WCSAXES", 2)
            .Real("CRPIX1", 1)
            .Real("CRPIX2", 1)
            .Real("CRVAL1", 10)
            .Real("CRVAL2", 20)
            .Real("CDELT1", 2)
            .Real("CDELT2", 3)
            .Real("PC1_1", 1)
            .Real("PC2_2", 1)
            .Text("CTYPE1", "FREQ")
            .Text("CTYPE2", "TIME")
            .Integer("WCSAXESA", 2)
            .Real("CRVAL1A", 100)
            .Text("CTYPE1A", "WAVE")
            .Build();

        double[] actual = FitsWorld.FromHeader(header, 'A').PixelToWorld([2, 2]);

        Assert.Equal(102, actual[0], 12);
        Assert.Equal(23, actual[1], 12);
        Assert.Equal("WAVE", FitsWorld.FromHeader(header, 'A').AxisMetadata[0].Type);
        Assert.Equal("TIME", FitsWorld.FromHeader(header, 'A').AxisMetadata[1].Type);
    }

    [Fact]
    public void AlternateWcsUsesImageAxisCountWhenWcsaxesIsAbsent()
    {
        var world = FitsWorld.FromHeader(FitsHeader.Builder()
            .Integer("NAXIS", 2)
            .Text("CTYPE1", "FREQ")
            .Text("CTYPE2", "TIME")
            .Build(), 'A');

        Assert.Equal(2, world.Axes);
    }

    [Fact]
    public void ExposesPaperAxisMetadataAndRejectsNonfiniteMetadata()
    {
        var world = FitsWorld.FromHeader(FitsHeader.Builder()
            .Integer("WCSAXES", 1)
            .Text("CTYPE1", "FREQ")
            .Text("CUNIT1", "Hz")
            .Text("CNAME1", "observed frequency")
            .Real("CRDER1", 0.25)
            .Real("CSYER1", 0.5)
            .Build());

        WcsAxisMetadata axis = world.AxisMetadata[0];
        Assert.Equal("observed frequency", axis.Name);
        Assert.Equal(0.25, axis.RandomError!.Value, 12);
        Assert.Equal(0.5, axis.SystematicError!.Value, 12);

        Assert.Throws<FitsException>(() => FitsWorld.FromHeader(FitsHeader.Builder()
            .Integer("WCSAXES", 1)
            .Real("CRDER1", double.NaN)
            .Build()));
    }

    [Fact]
    public void RejectsCelestialProjectionCoordinatesOutsideTheirDomain()
    {
        var world = FitsWorld.FromHeader(FitsHeader.Builder()
            .Integer("WCSAXES", 2)
            .Real("CRPIX1", 1)
            .Real("CRPIX2", 1)
            .Real("CRVAL1", 0)
            .Real("CRVAL2", 0)
            .Real("CDELT1", 100)
            .Real("CDELT2", 100)
            .Text("CTYPE1", "RA---SIN")
            .Text("CTYPE2", "DEC--SIN")
            .Build());

        Assert.Throws<FitsException>(() => world.PixelToWorld([2, 2]));
    }

    [Fact]
    public void RecognizesPaperThreeSpectralAlgorithmWithoutTreatingItAsCelestialProjection()
    {
        var world = FitsWorld.FromHeader(FitsHeader.Builder()
            .Integer("WCSAXES", 1)
            .Text("CTYPE1", "FREQ-LOG")
            .Text("CUNIT1", "Hz")
            .Build());

        Assert.True(world.AxisMetadata[0].IsSpectral);
        Assert.Equal("LOG", world.AxisMetadata[0].Algorithm);
    }

    [Fact]
    public void RecognizesPaperFourTabulatedTimeAlgorithm()
    {
        var world = FitsWorld.FromHeader(FitsHeader.Builder()
            .Integer("WCSAXES", 1)
            .Text("CTYPE1", "TIME-TAB")
            .Build());

        Assert.True(world.AxisMetadata[0].IsTime);
        Assert.Equal("TAB", world.AxisMetadata[0].Algorithm);
    }

    [Fact]
    public void AppliesPaperThreeLogarithmicSpectralConventionAndInvertsIt()
    {
        var world = FitsWorld.FromHeader(FitsHeader.Builder()
            .Integer("WCSAXES", 1)
            .Real("CRPIX1", 1)
            .Real("CRVAL1", 100)
            .Real("CDELT1", 1)
            .Text("CTYPE1", "FREQ-LOG")
            .Text("CUNIT1", "Hz")
            .Text("SPECSYS", "TOPOCENT")
            .Real("RESTFRQ", 1.420405751e9)
            .Build());

        Assert.Equal(100, world.PixelToWorld([1])[0], 12);
        Assert.Equal(101.00501670841679, world.PixelToWorld([2])[0], 12);
        Assert.Equal(2, world.WorldToPixel([101.005016708333])[0], 9);
        Assert.Equal("TOPOCENT", world.AxisMetadata[0].ReferenceSystem);
        Assert.True(Math.Abs(1.420405751e9 - world.AxisMetadata[0].RestFrequency!.Value) < 0.001);
    }

    [Fact]
    public void RejectsAZeroReferenceValueForLogarithmicSpectralAxes()
    {
        FitsWorld world = FitsWorld.FromHeader(FitsHeader.Builder()
            .Integer("WCSAXES", 1)
            .Real("CRVAL1", 0)
            .Real("CDELT1", 1)
            .Text("CTYPE1", "FREQ-LOG")
            .Build());

        Assert.Throws<FitsException>(() => world.PixelToWorld([1]));
    }
}
