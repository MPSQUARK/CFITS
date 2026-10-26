# CFITS FITS 4.0 Compliance Traceability Matrix

This matrix records the current repository evidence against the normative FITS
Standard Version 4.0. It is a traceability baseline for the approved
compliance TDD plan, not a claim of complete conformance.

## Normative references and status meanings

- [FITS Standard Version 4.0, language-edited PDF](https://fits.gsfc.nasa.gov/standard40/fits_standard40aa-le.pdf)
  (IAU FITS Working Group; approved 2016-07-22, language-edited 2018-08-13).
- [NASA FITS Standard page](https://fits.gsfc.nasa.gov/fits_standard.html).
- [IAU Registry of FITS Conventions](https://fits.gsfc.nasa.gov/fits_registry.html).
  The registry identifies six conventions as incorporated into FITS 4.0 and
  states that the Standard supersedes registry wording where they differ.

Status is evidence-based:

- **covered**: an implementation path and focused test evidence are present,
  with no known scope limitation in this row.
- **partial**: an implementation slice is present, but important requirements,
  edge cases, validation, or write/read symmetry remain untested or absent.
- **missing**: no implementation or test evidence was found.

Paths are repository-relative. Proposed red-test names identify remaining
coverage targets; some have since been implemented by the compliance TDD work.

## Core FITS Standard

| Requirement | Standard section | Current implementation path | Existing test path | Gap status | Proposed red test |
|---|---|---|---|---|---|
| FITS file is a sequence of 2880-byte logical records; cards are 80 bytes | 3.1, 3.2, 4.1 | `Source/CFITS/Reading/FitsReader.cs`; `Source/CFITS/Core/FitsBlock.cs`; `Source/CFITS/Document/FitsDocument.cs` | `Tools/CFITS.Tests/Headers/HeaderSpecTests.cs` (`HeaderCardsAndBlocksUseTheRequiredGeometry`, `AlignsEveryHduHeaderAndDataAreaToFITSBlocks`) | covered | `RejectsHeaderOrDataRecordWithNon2880Alignment` |
| Primary HDU starts with `SIMPLE`; extensions start with `XTENSION`; HDUs/data are sequenced correctly | 3.3, 4.4.1, 7.1–7.3 | `Source/CFITS/Reading/FitsReader.cs`; `Source/CFITS/Layout/DataLayout.cs` | `Tools/CFITS.Tests/Compliance/CoreFits40MatrixGapTests.cs`; `Tools/CFITS.Tests/Compliance/Fits40HeaderComplianceTests.cs`; `Tools/CFITS.Tests/Compliance/Fits40ImageExtensionComplianceTests.cs` | covered | — |
| Card syntax, keyword names, comments, ASCII, and `END` padding | 4.1–4.4, Appendix A, Appendix D | `Source/CFITS/Header/FitsCardView.cs`; `Source/CFITS/Header/CardImages.cs`; `Source/CFITS/Header/FitsHeader.cs` | `Tools/CFITS.Tests/Headers/HeaderSpecTests.cs`; `Tools/CFITS.Tests/Compliance/CoreFits40MatrixGapTests.cs` | covered | — |
| Character, logical, integer, real, complex, undefined, and date values | 4.2.1–4.2.7 | `Source/CFITS/Header/FitsCardView.cs`; `Source/CFITS/Header/FitsValueKind.cs`; `Source/CFITS/Header/FitsCard.cs` | `Tools/CFITS.Tests/Headers/HeaderSpecTests.cs`; `Tools/CFITS.Tests/Compliance/CoreFits40MatrixGapTests.cs` | covered | — |
| Reserved and mandatory keywords are interpreted with required semantics | 4.4.1, 4.4.2, Appendix C | `Source/CFITS/Layout/DataLayout.cs`; `Source/CFITS/Header/FitsHeader.cs`; `Source/CFITS/Document/FitsDocumentBuilder.cs` | `Tools/CFITS.Tests/Compliance/Fits40HeaderComplianceTests.cs`; `Tools/CFITS.Tests/Compliance/Fits40ImageExtensionComplianceTests.cs`; `Tools/CFITS.Tests/Compliance/CoreFits40MatrixGapTests.cs` | covered | — |
| Big-endian data representation and supported BITPIX values | 5.1–5.3, Appendix E | `Source/CFITS/Data/Pixels.cs`; `Source/CFITS/Data/FitsImage.cs`; `Source/CFITS/Document/FitsDocumentBuilder.cs` | `Tools/CFITS.Tests/Compliance/BitpixByteVectorComplianceTests.cs`; `Tools/CFITS.Tests/ImageSpecTests.cs` | covered | — |
| Image HDU dimensions, data size, scaling, and undefined integer pixels | 7.1.1–7.1.3; 5.4 | `Source/CFITS/Data/FitsImage.cs`; `Source/CFITS/Layout/DataLayout.cs`; `Source/CFITS/Reading/FitsReader.cs` | `Tools/CFITS.Tests/ImageSpecTests.cs` | covered | `RejectsImageHeaderWithOverflowingProductOrIllegalScalingCombination` |
| Random-groups keywords, parameter/data sequence, scaling, repeated parameter names, and padding | 6.1–6.3 | `Source/CFITS/Data/FitsGroups.cs`; `Source/CFITS/Layout/DataLayout.cs`; `Source/CFITS/Reading/FitsReader.cs`; `Source/CFITS/Document/FitsDocumentBuilder.cs` | `Tools/CFITS.Tests/Compliance/RandomGroupsSectionSixComplianceTests.cs`; `Tools/CFITS.Tests/Compliance/Fits40RandomGroupsComplianceTests.cs`; `Tools/CFITS.Tests/Compliance/RandomGroupsEdgeComplianceTests.cs`; `Tools/CFITS.Tests/Fixtures/DifferentialFixtureTests.cs`; `Tools/CFITS.Tests/Fixtures/OracleDump.cs`; `SampleData/Generated/groups.fits` | covered | — |
| ASCII-table mandatory keywords, field formats, rows, entries, gaps, precision, and raw-cell symmetry | 7.2.1–7.2.5 | `Source/CFITS/Data/ColumnFormats.cs`; `Source/CFITS/Document/AsciiTableBuilder.cs`; `Source/CFITS/Document/FitsRawTableBuilder.cs`; `Source/CFITS/Data/FitsTable.cs` | `Tools/CFITS.Tests/TableSpecTests.cs`; `Tools/CFITS.Tests/Compliance/AsciiTableComplianceTests.cs`; `Tools/CFITS.Tests/Compliance/ExhaustiveTableSchemaTests.cs`; `Tools/CFITS.Tests/Compliance/ExternalTableFixtureTests.cs` | covered | — |
| Binary-table formats, row layout, vectors, complex values, bits, nulls, TDIM, and raw-cell symmetry | 7.3.1–7.3.4 | `Source/CFITS/Data/ColumnFormats.cs`; `Source/CFITS/Document/BinaryTableBuilder.cs`; `Source/CFITS/Document/FitsRawTableBuilder.cs`; `Source/CFITS/Data/FitsColumn.cs` | `Tools/CFITS.Tests/TableSpecTests.cs`; `Tools/CFITS.Tests/Data/FitsColumnBulkTests.cs`; `Tools/CFITS.Tests/Compliance/ExhaustiveTableSchemaTests.cs`; `Tools/CFITS.Tests/Compliance/ExternalTableFixtureTests.cs` | covered | — |
| Binary-table variable-length arrays, typed vectors, raw heap bytes, row dimensions, and THEAP/PCOUNT bounds | 7.3.5, 7.3.6 | `Source/CFITS/Data/FitsColumn.cs`; `Source/CFITS/Document/BinaryTableBuilder.cs`; `Source/CFITS/Document/FitsRawTableBuilder.cs`; `Source/CFITS/Layout/DataLayout.cs` | `Tools/CFITS.Tests/TableSpecTests.cs`; `Tools/CFITS.Tests/Compliance/VariableArrayComplianceTests.cs`; `Tools/CFITS.Tests/Compliance/BinaryTableComplianceTests.cs`; `Tools/CFITS.Tests/Compliance/ExhaustiveTableSchemaTests.cs`; `Tools/CFITS.Tests/Compliance/ExternalTableFixtureTests.cs` | covered | — |
| WCS linear, celestial, spectral, and alternate-axis representations | 8.1–8.5 | `Source/CFITS/Core/FitsWorld.cs`; `Source/CFITS/Core/WcsAxisMetadata.cs` | `Tools/CFITS.Tests/Fixtures/DifferentialFixtureTests.cs`; `Tools/CFITS.Tests/Compliance/WcsSectionEightNineReferenceVectorTests.cs`; `Tools/CFITS.Tests/Fixtures/DifferentialFixtures.json` | covered | — |
| Time-coordinate values, reference frames, scales, offsets, and units | 9.1–9.8 | `Source/CFITS/Core/FitsClock.cs`; `Source/CFITS/Core/IersEarthOrientationProvider.cs`; `Source/CFITS/Core/JplEphemerisProvider.cs` | `Tools/CFITS.Tests/Compliance/TimeScaleConventionTests.cs`; `Tools/CFITS.Tests/Compliance/TimeReferenceConventionTests.cs`; `Tools/CFITS.Tests/Compliance/TimeAxisConventionTests.cs`; `Tools/CFITS.Tests/Compliance/TimeSectionNineCoverageTests.cs` | covered | — |
| FITS exceptions identify malformed location/context without accepting truncation | 3.1–3.3, 4.4, 7.1–7.3 | `Source/CFITS/Core/FitsException.cs`; `Source/CFITS/Core/FitsLocation.cs`; `Source/CFITS/Reading/FitsReader.cs` | `Tools/CFITS.Tests/Validation/ValidationRegressionTests.cs`; `Tools/CFITS.Tests/Compliance/CoreFits40MatrixGapTests.cs`; header/image tests | covered | — |

## Incorporated official conventions

The registry lists these six conventions as reviewed and incorporated into the
FITS 4.0 Standard. They are tracked separately because their detailed
requirements are distributed across the Standard and convention documents.

| Incorporated convention | Standard section | Current implementation path | Existing test path | Gap status | Proposed red test |
|---|---|---|---|---|---|
| Header space convention for reserving future header cards | 4.4.3; registry “Header space convention” | `Source/CFITS/Header/FitsHeaderBuilder.cs` (`Reserve`, `Replace`, `Insert`) and `Source/CFITS/Header/CardImages.cs` | `Tools/CFITS.Tests/Headers/HeaderSpecTests.cs` (`ReservesHeaderSpaceThenReplacesCardsWithoutMovingData`, `RejectsAReservedReplacementThatNeedsMultiplePhysicalCards`) | covered | `PreservesReservedHeaderSpaceAndAllowsLaterKeywordInsertion` |
| CONTINUE long-string keyword convention | 4.2.1, 4.4.3; registry “CONTINUE Long String Keyword convention” | `Source/CFITS/Header/FitsCardView.cs`; `Source/CFITS/Header/FitsHeader.cs`; `Source/CFITS/Header/CardImages.cs` | `Tools/CFITS.Tests/Headers/HeaderSpecTests.cs` (`CombinesContinuedLongStringCards`, `WritesAndReadsLongStringsAtEveryContinuationBoundary`, `PreservesContinuationCommentsAndPhysicalCardGeometry`, `TreatsAnOrphanContinueCardAsCommentary`, `TreatsAnAmpersandWithoutAFollowingContinueAsLiteral`) | covered | — |
| Column Limits keywords (`TLMINn`/`TLMAXn`, `TDMINn`/`TDMAXn`) | 4.4.2; registry “Column Limits keywords” | `Source/CFITS/Data/FitsTable.cs` validates scalar/vector limit declarations | `Tools/CFITS.Tests/Compliance/ColumnLimitsComplianceTests.cs` | covered | `ReadsAndValidatesColumnLimitsForScalarAndVectorColumns` |
| CHECKSUM and DATASUM integrity convention | 4.4.2.7, Appendix J; registry “CHECKSUM keyword convention” | `Source/CFITS/Core/FitsChecksum.cs`; `Source/CFITS/Core/FitsIntegrity.cs`; `Source/CFITS/Document/FitsDocument.cs` | `Tools/CFITS.Tests/Checksums/ChecksumTests.cs` (`EncodingMatchesTheOfficialFITSReferenceVector`, `StampingPlacesIntegrityKeywordsBeforeEndAndKeepsThemCardAligned`, `StampedImageMatchesItsKeywordsAndAstropy`); `Tools/CFITS.Tests/Compliance/IntegrityAndPaddingComplianceTests.cs` | covered | — |
| Tiled image compression | 10.1–10.2, 10.4; registry “Tiled Image Compression” | `Source/CFITS/Compression/TileCompression.cs`; `Source/CFITS/Compression/Rice.cs`; `Source/CFITS/Compression/HCompress.cs`; `Source/CFITS/Document/FitsDocumentBuilder.cs` | `Tools/CFITS.Tests/Fixtures/DifferentialFixtureTests.cs`; `Tools/CFITS.Tests/Compression/CompressionTests.cs`; `Tools/CFITS.Tests/Compliance/CompressionEdgeComplianceTests.cs`; `Tools/CFITS.Tests/Fixtures/DifferentialFixtures.json` | covered | — |
| Tiled table compression | 10.3–10.4; registry “Tiled Table Compression” | `Source/CFITS/Compression/TableCompression.cs`; `Source/CFITS/Data/FitsTable.cs` | `Tools/CFITS.Tests/Fixtures/DifferentialFixtureTests.cs`; `Tools/CFITS.Tests/Compression/TableCompressionTests.cs`; `Tools/CFITS.Tests/Compliance/OfficialCompressedTableTests.cs`; `Tools/CFITS.Tests/Fixtures/DifferentialFixtures.json` | covered | — |

## Highest-risk gaps

1. **Mandatory-keyword and structural validation** is broad but not yet
   demonstrated as a complete per-HDU-kind validator. Invalid combinations can
   therefore be accepted even when ordinary examples parse successfully.
2. **Binary-table edge coverage** is high risk because `TFORMn`, variable-length
   arrays, heap bounds, scaling, nulls, and multidimensional descriptors
   interact; the current tests cover representative builders rather than the
   full legal/illegal grammar.
3. **Compression interoperability** is covered for the supported algorithms,
   tile edges, directive settings, variable-length columns, and checked-in
   Astropy/CFITSIO-compatible fixtures. Unsupported algorithms still fail
   explicitly.
4. **WCS and time** have deterministic offline reference vectors and focused
   malformed-input checks. The evidence is bounded to the implemented
   Sections 8 and 9 model rather than every optional FITS convention.
5. The matrix has no remaining `partial` or `missing` status rows. The
   limitations below describe scope boundaries, not absent differential
   evidence.

## Audit findings addendum

This addendum records the completed focused audits. Priorities indicate the
risk of accepting or producing a file that is structurally invalid or
interoperable only with this implementation:

- **P0**: must be resolved before claiming FITS-compliant interchange.
- **P1**: important interoperability or standards-coverage gap, but not an
  immediate blocker for the currently exercised path.

| Priority | Audit finding and evidence | Standard section | Existing evidence | Proposed red test |
|---|---|---|---|---|
| P0 | **Header validation now enforces the core per-HDU mandatory structure.** `FitsReader` identifies the primary and extension starts, while `DataLayout` validates required ordering, types, extension counts, random-groups constraints, and image-extension `PCOUNT`/`GCOUNT` semantics. Table-specific mandatory keywords remain validated by `FitsTable`. | 3.3; 4.4.1–4.4.2; 7.1.1–7.3.2 | `Source/CFITS/Reading/FitsReader.cs`; `Source/CFITS/Layout/DataLayout.cs`; `Source/CFITS/Header/FitsHeader.cs`; `Tools/CFITS.Tests/Compliance/Fits40HeaderComplianceTests.cs`; `Tools/CFITS.Tests/Compliance/Fits40ImageExtensionComplianceTests.cs`; `Tools/CFITS.Tests/Compliance/CoreFits40MatrixGapTests.cs` | covered |
| P0 | **Binary-table heap safety is covered for hostile descriptors.** Descriptor counts, offsets, `PCOUNT`, `THEAP`, row geometry, maximum lengths, and exact-end arrays are validated before access. | 7.3.1–7.3.6 | `Source/CFITS/Data/FitsColumn.cs`; `Source/CFITS/Data/FitsTable.cs`; `Source/CFITS/Layout/DataLayout.cs`; `Source/CFITS/Document/BinaryTableBuilder.cs`; `Source/CFITS/Document/FitsRawTableBuilder.cs`; `Tools/CFITS.Tests/Compliance/BinaryTableComplianceTests.cs`; `Tools/CFITS.Tests/Compliance/VariableArrayComplianceTests.cs`; `Tools/CFITS.Tests/Compliance/ExhaustiveTableSchemaTests.cs` | covered |
| P0 | **Supported compressed-image interoperability is covered by offline differential fixtures.** The implementation accepts the checked-in `RICE_1`, `GZIP_1`, `GZIP_2`, `PLIO_1`, and `HCOMPRESS_1` cases, including tile edges and Astropy quantization directives; unsupported algorithms fail explicitly. | 10.1.1–10.2.2; 10.4.1–10.4.4 | `Source/CFITS/Compression/TileCompression.cs`; `Source/CFITS/Compression/Rice.cs`; `Source/CFITS/Compression/HCompress.cs`; `Tools/CFITS.Tests/Fixtures/DifferentialFixtureTests.cs`; `Tools/CFITS.Tests/Fixtures/DifferentialFixtures.json` | covered |
| P0 | **Supported compressed-table interoperability is covered by offline differential fixtures.** The checked-in cases exercise directive keywords, column restoration, `GZIP_1`/`GZIP_2`/`RICE_1`, and variable-length `P`/`Q` descriptors; malformed directives remain explicit rejection tests. | 10.3.1–10.3.6; 10.4 | `Source/CFITS/Compression/TableCompression.cs`; `Source/CFITS/Data/FitsTable.cs`; `Tools/CFITS.Tests/Fixtures/DifferentialFixtureTests.cs`; `Tools/CFITS.Tests/Compliance/OfficialCompressedTableTests.cs`; `Tools/CFITS.Tests/Fixtures/DifferentialFixtures.json` | covered |
| P1 | **Header-space convention now has a fixed-slot API and tests.** Remaining review is limited to broader interoperability fixtures. | 4.4.3; incorporated Header Space convention | `Source/CFITS/Header/CardImages.cs`; `Source/CFITS/Header/FitsHeaderBuilder.cs`; `Tools/CFITS.Tests/Headers/HeaderSpecTests.cs` | `ReservesHeaderSpaceThenInsertsCardsWithoutMovingData` |
| P1 | **CONTINUE coverage now includes write/read boundary splitting, continuation comments, escaped quotes, malformed chains, literal trailing ampersands, and exact 80-byte card geometry.** | 4.2.1; 4.4.3; incorporated CONTINUE convention | `Source/CFITS/Header/FitsCardView.cs`; `Source/CFITS/Header/FitsHeader.cs`; `Source/CFITS/Header/CardImages.cs`; `Tools/CFITS.Tests/Headers/HeaderSpecTests.cs` | covered | — |
| P1 | **Column Limits convention has scalar/vector validation and focused tests.** Broader external fixture coverage remains useful. | 4.4.2; incorporated Column Limits convention | `Source/CFITS/Data/FitsTable.cs`; `Tools/CFITS.Tests/Compliance/ColumnLimitsComplianceTests.cs` | `ReadsColumnLimitsAndRejectsInvalidScalarVectorLimitDeclarations` |
| P1 | **CHECKSUM coverage now includes the official encoding reference vector, DATASUM/CHECKSUM placement, read/write symmetry, malformed and duplicate states, and header/data/padding tamper detection.** | 4.4.2.7; Appendix J; incorporated CHECKSUM convention | `Source/CFITS/Core/FitsChecksum.cs`; `Source/CFITS/Core/FitsIntegrity.cs`; `Source/CFITS/Document/FitsDocument.cs`; `Tools/CFITS.Tests/Checksums/ChecksumTests.cs`; `Tools/CFITS.Tests/Compliance/IntegrityAndPaddingComplianceTests.cs` | covered | — |
| P1 | **Random-groups Section 6 coverage is complete for the supported public model.** Mandatory ordering and dimensions, zero-group records, parameter/data sequence, all legal BITPIX values, PSCAL/PZERO scaling, repeated PTYPEn addends, malformed payloads, zero padding, and deterministic Astropy/CFITSIO-compatible fixture provenance are covered by implementation and focused tests. | 6.1–6.3 | `Source/CFITS/Data/FitsGroups.cs`; `Source/CFITS/Layout/DataLayout.cs`; `Source/CFITS/Reading/FitsReader.cs`; `Source/CFITS/Document/FitsDocumentBuilder.cs`; `Tools/CFITS.Tests/Compliance/RandomGroupsSectionSixComplianceTests.cs`; `Tools/CFITS.Tests/Compliance/Fits40RandomGroupsComplianceTests.cs`; `Tools/CFITS.Tests/Fixtures/DifferentialFixtureTests.cs`; `Tools/CFITS.Tests/Fixtures/OracleDump.cs`; `SampleData/Generated/groups.fits` | covered |
| P1 | **Implemented WCS interoperability is covered by offline reference fixtures.** Linear, celestial projection, matrix, spectral metadata, alternate descriptions, and invalid combinations are checked against Astropy/WCSLIB vectors; optional conventions outside the implemented model remain out of scope. | 8.1–8.5 | `Source/CFITS/Core/FitsWorld.cs`; `Tools/CFITS.Tests/Fixtures/DifferentialFixtureTests.cs`; `Tools/CFITS.Tests/Compliance/WcsSectionEightNineReferenceVectorTests.cs`; `Tools/CFITS.Tests/Fixtures/DifferentialFixtures.json` | covered |
| P1 | **Time Sections 9.1–9.8 are covered by metadata, conversion, malformed-input, property, and deterministic provider-vector evidence.** The implementation preserves caller-supplied providers while also exposing versioned offline IERS-compatible and JPL-compatible compact reference providers with explicit out-of-range failure. | 9.1–9.8 | `Source/CFITS/Core/FitsClock.cs`; `Source/CFITS/Core/IersEarthOrientationProvider.cs`; `Source/CFITS/Core/JplEphemerisProvider.cs`; `Tools/CFITS.Tests/Compliance/TimeSectionNineCoverageTests.cs`; `Tools/CFITS.Tests/Compliance/TimeScaleConventionTests.cs`; `Tools/CFITS.Tests/Compliance/TimeReferenceConventionTests.cs`; `Tools/CFITS.Tests/Compliance/TimeAxisConventionTests.cs` | covered |

These findings do not change the matrix statuses above; they make the
implementation and test evidence behind the highest-risk statuses explicit.

## Current compliance-pass results

The following gaps were closed by focused regression tests and implementation
changes in this pass:

- Fixed-bit `X` columns now address every declared bit, including bits after
  the first stored byte.
- Random-groups primary HDUs accept the legal zero-group case
  (`GCOUNT = 0`) when no group records are present, while non-group HDUs still
  require `GCOUNT = 1`.
- Random-groups builders now also emit the legal `GCOUNT = 0`, `PCOUNT > 0`
  shape when parameter declarations exist but no group records are present;
  `Tools/CFITS.Tests/Compliance/TableAndGroupsRemainingGapTests.cs` verifies
  the resulting parameter metadata and empty payload.
- Table creation now validates the table-specific `XTENSION`, `BITPIX`,
  `GCOUNT`, and ASCII `PCOUNT` invariants.
- Random-group parameter keywords beyond `PCOUNT` are rejected.
- Compressed-table reconstruction emits the mandatory logical table geometry
  before it is revalidated.
- Double-precision complex (`M`) table values and fixed bit vectors have
  focused read tests.

These changes do not establish full conformance. Remaining limitations are:

1. The checked-in differential corpus records Astropy 8.0.2 provenance and
   CFITSIO-compatible FITS wire format in
   `Tools/CFITS.Tests/Fixtures/DifferentialFixtures.json`. CFITSIO binaries
   were unavailable in the current Windows environment; the importer therefore
   verifies hashes, parses the fixtures, and checks decoded reference values
   entirely offline without claiming execution by an unavailable binary.
2. The raw schema writer accepts arbitrary legal table schemas and exact
   fixed-row/heap bytes; existing typed overloads remain available for common
   cases.
3. Variable-array `TDIM` semantics are exposed through
   `FitsColumn.ReadVariableDimensions(row)` and return null when a row's
   descriptor count does not match the declared shape.
4. ASCII numeric formatting remains owned by the existing typed builder;
   arbitrary ASCII numeric wire values are supported through the raw schema
   path.
5. Compression and WCS rows are covered for the implemented model and checked-in
   supported cases. Full convention-level coverage beyond that model remains
   outside this audit.
6. Random-groups Section 6 is covered for the supported API and wire format;
   the standard itself deprecates random groups and recommends binary tables
   for new applications.

## WCS/time provider evidence

`FitsClock` accepts `TDB` and `TCB` header scales when an
`IEphemerisProvider` is supplied through `FitsClockOptions`; without one it
fails at the header boundary with an explicit ephemeris requirement. Provider
results are checked for finite values, and the selected provider release is
exposed through `EphemerisVersion` and `EarthOrientationVersion`.
`IersEarthOrientationProvider.Default` is the offline
`IERS-finals2000A.compact-2026a` vector provider, and
`JplEphemerisProvider.Default` is the offline `JPL-DE440.compact-2026a`
vector provider. Both providers interpolate only inside their checked-in
sample coverage and return false outside it; `FitsClock` converts that result
to a deterministic FITS exception. Applications can still supply any
versioned provider implementing the existing contracts. The focused evidence
is `Tools/CFITS.Tests/Compliance/TimeSectionNineCoverageTests.cs` together
with `WcsSectionEightNineReferenceVectorTests` time-provider tests.

