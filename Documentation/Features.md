# CFITS Features

CFITS reads and writes FITS on .NET. This page is a quick capability check; worked examples live in the [README](../README.md), and FITS 4.0 traceability is in the [compliance matrix](FITS-Compliance-Matrix.md).

## Typical applications

- Desktop tools, services, and batch jobs on observatory FITS without a Python dependency.
- Document-level access (`FitsDocument`), typed layouts (`CFITS.Generator`), or a raw scan (`FitsReader`).
- Multi-extension files opened HDU-by-HDU when you do not need the whole file in memory.
- Image decode into a caller-owned buffer (BOYB) instead of a new `float[,]`.
- .NET 10; usable with Native AOT after you validate your FITS paths.

## Read / write coverage

| | Read | Write |
|---|:---:|:---:|
| Primary and image extensions (standard BITPIX values) | Yes | Yes |
| ASCII tables | Yes | Yes |
| Binary tables (including vectors, bit columns, variable-length heap data) | Yes | Yes |
| Compressed tables | Yes | Partial |
| Random groups | Yes | Yes |
| Tiled compressed images (GZIP, Rice, HCOMPRESS, PLIO, …) | Yes | No |
| CONTINUE strings and reserved header slots | Yes | Yes |
| CHECKSUM / DATASUM | Yes | Yes |
| Bytes after the last HDU (special records) | Yes | Yes |

Compressed image tiles from Rice, HCOMPRESS, or PLIO are read-only here; producing those tiles is left to CFITSIO, Astropy, or similar. Other unsupported formats are rejected explicitly rather than mis-read.

## Entry points

**`FitsDocument.Open`** — headers plus `Image`, `Table`, or `Groups` on each HDU.

**`FitsParser` / `[FitsDocument]`** — deserialize a mapped type, or use generated `Open` when you only need one extension.

**`FitsDocumentBuilder`** — assemble a new file (images, tables, checksum stamping).

**`FitsReader`** — lightweight token pass over a byte span.

**`FitsImage.Describe` / `FitsImage.Read`** — BOYB: layout query and decode into your buffer (plain or compressed images).

Reads apply BSCALE, BZERO, and BLANK by default (`FitsScaleMode.Physical`). Use stored mode when you want on-disk values.

## WCS, time, errors

**`FitsWorld`** handles linear WCS and the celestial projections documented in [WCS-Projection-Reference.md](WCS-Projection-Reference.md).

**`FitsClock`** covers FITS time keywords and scale conversion; plug in an ephemeris provider for TDB/TCB if you need it.

Invalid structure surfaces as **`FitsException`** with HDU, keyword, or column context when available.

## NuGet

**`CFITS`** — runtime library (required).

**`CFITS.Generator`** — optional analyzer for typed documents; does not replace the runtime package. Mapping mistakes are compile errors; the list is in [Generator-Diagnostics.md](Generator-Diagnostics.md).
