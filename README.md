# CFITS

A .NET library for reading and writing [FITS](https://fits.gsfc.nasa.gov/) files: headers, images, ASCII and binary tables, heaps, random groups, and tiled compression (IAU standard through chapter 7).

The surface area mirrors `System.Text.Json`: `FitsReader` for low-level tokens, `FitsDocument` for indexed HDUs, and `FitsSerializer` with `CFITS.Generator` for typed models.

**Requirements:** [.NET 10 SDK](https://dotnet.microsoft.com/download) or later.

## Performance

CFITS compared with Astropy and CSharpFITS.

<br/>

<table>
  <tr>
    <td align="center" style="padding: 20px 16px;">
      <b>Read / write time</b><br/><br/>
      <img src="Benchmarks/Plots/fits-comparison.png" alt="Read and write time" width="420" style="margin: 8px 0;" />
    </td>
    <td align="center" style="padding: 20px 16px;">
      <b>Peak memory</b><br/><br/>
      <img src="Benchmarks/Plots/fits-comparison-memory.png" alt="Peak memory" width="420" style="margin: 8px 0;" />
    </td>
  </tr>
</table>

<br/>

<small>Figures are meant for rough comparison only. Astropy runs through Python-side harnesses while CFITS is timed in-process on .NET, so wall-clock and memory numbers are not directly equivalent.</small>

## Install

NuGet ([`CFITS`](https://www.nuget.org/packages/CFITS), [`CFITS.Generator`](https://www.nuget.org/packages/CFITS.Generator)):

```shell
dotnet add package CFITS
dotnet add package CFITS.Generator
```

Reference both packages when you use source generation; the generator does not replace the runtime library.

From source:

```shell
git clone https://github.com/MPSQUARK/CFITS.git
cd CFITS
dotnet build CFITS.slnx
```

## Examples

### Runtime parsing

`FitsDocument` is trim-safe and works without the generator.

```csharp
using CFITS;

using var document = FitsDocument.Open("exposure.fits");
FitsHdu primary = document.Hdus[0];

double exptime = primary.Header.GetDouble("EXPTIME");
float corner = (float)primary.Image!.ReadPhysical(0);
```

### Source-generated serialization

Annotate types, add a partial context, and call `FitsSerializer`:

```csharp
using CFITS;

[FitsImage("EXPOSURE")]
public sealed class Exposure
{
    [FitsKeyword("EXPTIME")]
    public double ExposureSeconds { get; set; }

    [FitsData]
    public float[,]? Pixels { get; set; }
}

[FitsTable("EVENTS")]
public sealed class EventRow
{
    [FitsColumn("TIME")]
    public double Time { get; set; }

    [FitsColumn("PHA")]
    public int Channel { get; set; }
}

[FitsSerializable(typeof(Exposure))]
[FitsSerializable(typeof(EventRow))]
public sealed partial class AppFitsContext : FitsSerializerContext;

// Read / write
Exposure image = FitsSerializer.Read("exposure.fits", AppFitsContext.Default.Exposure);

FitsSerializer.Write("events.fits", new[]
{
    new EventRow { Time = 1.5, Channel = 4 },
    new EventRow { Time = 2.75, Channel = 9 },
}, AppFitsContext.Default.EventRow);
```

Add the generator as an analyzer (typical `PackageReference` with `PrivateAssets="all"` and `IncludeAssets` including `analyzers`).

### Native AOT

`CFITS` is trimmable and AOT-compatible. Enable `PublishAot` on your application, reference `CFITS.Generator` at build time for typed serialization, and validate trim/AOT against the FITS variants you use.

## Development

Tests use Python 3 + [Astropy](https://www.astropy.org/) oracles. With [uv](https://docs.astral.sh/uv/):

```powershell
cd Tools/CFITS.PyScripts
uv sync
```

```shell
dotnet test
```

Set `CFITS_PYTHON` if the host should not auto-discover `.venv`.

Benchmarks (`Tools/CFITS.Benchmarks`):

```shell
dotnet run --project Tools/CFITS.Benchmarks -c Release -- --compare
dotnet run --project Tools/CFITS.Benchmarks -c Release -- --replot
```
