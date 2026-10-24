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

- `src/CFITS` — the library, Native AOT compatible
- `src/CFITS.Generator` — the serializer source generator
- `tests/CFITS.Tests` — xUnit tests, including an Astropy oracle
- `tools/CFITS.Console` — `CFITS.Console file.fits` prints the HDUs
- `SampleGenerator` — Astropy fixture generator used by the tests

The tests expect the virtual environment at `.venv`, created with `py -3 -m venv .venv` and `pip install astropy numpy`.

```powershell
dotnet test
dotnet run --project tools/CFITS.Console -- path\to\file.fits
```
