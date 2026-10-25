# CFITS

A C# library for reading and writing FITS files through chapter 7 of the IAU standard: headers, images, ASCII tables, binary tables, the heap, and random groups.

The public shape follows `System.Text.Json`:

- `FitsReader` walks a file as tokens over a span.
- `FitsDocument` opens a file and indexes HDUs, keywords, and columns.
- `FitsSerializer` maps an HDU onto a class through a source-generated context.

```csharp
using var document = FitsDocument.Open("exposure.fits");
double exposure = document.Hdus[0].Header.GetDouble("EXPTIME");

var image = FitsSerializer.Read("exposure.fits", AppFitsContext.Default.Exposure);
```

Projects:

- `Source/CFITS` — the library, Native AOT compatible
- `Source/CFITS.Generator` — the serializer source generator
- `Tools/CFITS.Tests` — xUnit tests, including an Astropy oracle
- `Tools/CFITS.Console` — `CFITS.Console file.fits` prints the HDUs
- `Tools/CFITS.PyScripts` — Astropy fixtures, oracles, and benchmark tooling

The tests use Python 3 with Astropy and NumPy. They discover `.venv` on Windows or POSIX systems, or an executable configured through `CFITS_PYTHON`.
Install [uv](https://docs.astral.sh/uv/), then sync the Python tooling environment:

```powershell
cd Tools/CFITS.PyScripts
uv sync
uv run python Oracles\CheckAstropy.py
```

```powershell
dotnet test
dotnet run --project Tools/CFITS.Console -- path\to\file.fits
```
