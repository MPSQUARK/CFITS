# Generator diagnostics

Compile errors from `CFITS.Generator` when a `[FitsDocument]` model cannot be generated. All of these are in the 0.1.0 analyzer release. New IDs go in `AnalyzerReleases.Unshipped.md` until the next package version.

| ID | What it means |
|----|----------------|
| CFITS0001 | The document or generated type is not `partial`. |
| CFITS0002 | Some image or table members set an HDU index and others do not. Set every index, or omit them all and let declaration order assign them. |
| CFITS0003 | The same HDU index is used twice. |
| CFITS0004 | A table property is not a type with exactly one `IReadOnlyList<T>` or `List<T>` row property. |
| CFITS0005 | An image model does not have exactly one `[FitsData]` property. |
| CFITS0006 | `IgnoreOtherHdus` is false and there is no `[FitsOtherHdus]` property to receive the rest of the file. |
| CFITS0007 | Two members claim the same FITS role (two primary headers, two `[FitsOtherHdus]`, and so on). |
| CFITS0008 | One HDU has two typed keyword objects. |
| CFITS0009 | The type already has a member named `Header` or `PrimaryHeader`, which the generator would add. |
| CFITS0010 | A header model is not `partial`, so the generator cannot add its `Header` property. |
| CFITS0011 | `[FitsOtherHdus]` is not `IReadOnlyList<FitsHduSnapshot>`. |
| CFITS0012 | A member’s type or attribute is not one the generator knows how to map. |
| CFITS0013 | `[FitsDocument]` is on a nested type. Use a top-level type. |
| CFITS0014 | A table row type has no `[FitsColumn]` properties. |
| CFITS0015 | A mapped property has no public or internal setter. |
| CFITS0016 | An HDU index is negative. |
| CFITS0017 | `[FitsSpecialRecords]` is not `ReadOnlyMemory<byte>` or `byte[]`. |
| CFITS0018 | The document type is `static`. |
| CFITS0019 | `[FitsKeyword]` is on a table row. Put keywords on the table model or a `[FitsHeader]` object. |
| CFITS0020 | `[FitsOtherHdus]` is present while `IgnoreOtherHdus` is still true (the default). Set it to false to keep unmapped HDUs. |

The release-tracking files next to this page (`AnalyzerReleases.Shipped.md`, `AnalyzerReleases.Unshipped.md`) are what the Roslyn release analyzer reads at build time. This page is the one to read.
