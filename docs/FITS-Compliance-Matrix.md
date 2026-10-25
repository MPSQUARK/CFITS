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

Paths are repository-relative. Proposed red tests are intentionally phrased as
test cases; they are not added by this documentation change.

## Core FITS Standard

| Requirement | Standard section | Current implementation path | Existing test path | Gap status | Proposed red test |
|---|---|---|---|---|---|
| FITS file is a sequence of 2880-byte logical records; cards are 80 bytes | 3.1, 3.2, 4.1 | `Source/CFITS/Reading/FitsReader.cs`; `Source/CFITS/Core/FitsBlock.cs`; `Source/CFITS/Document/FitsDocument.cs` | `Tools/CFITS.Tests/Headers/HeaderSpecTests.cs` (`HeaderCardsAndBlocksUseTheRequiredGeometry`, `AlignsEveryHduHeaderAndDataAreaToFITSBlocks`) | covered | `RejectsHeaderOrDataRecordWithNon2880Alignment` |
| Primary HDU starts with `SIMPLE`; extensions start with `XTENSION`; HDUs/data are sequenced correctly | 3.3, 4.4.1, 7.1–7.3 | `Source/CFITS/Reading/FitsReader.cs`; `Source/CFITS/Layout/DataLayout.cs` | `Tools/CFITS.Tests/Headers/HeaderSpecTests.cs`; `Tools/CFITS.Tests/Headers/HeaderTests.cs` | partial | `RejectsPrimaryAndExtensionMandatoryKeywordViolations` |
| Card syntax, keyword names, comments, ASCII, and `END` padding | 4.1–4.4, Appendix A, Appendix D | `Source/CFITS/Header/FitsCardView.cs`; `Source/CFITS/Header/CardImages.cs`; `Source/CFITS/Header/FitsHeader.cs` | `Tools/CFITS.Tests/Headers/HeaderSpecTests.cs`; `Tools/CFITS.Tests/Headers/HeaderTests.cs` | partial | `RejectsInvalidKeywordNameCommentDelimiterAndNonAsciiCard` |
| Character, logical, integer, real, complex, undefined, and date values | 4.2.1–4.2.7 | `Source/CFITS/Header/FitsCardView.cs`; `Source/CFITS/Header/FitsValueKind.cs`; `Source/CFITS/Header/FitsCard.cs` | `Tools/CFITS.Tests/Headers/HeaderSpecTests.cs` (`ParsesLegalLogicalNumericComplexStringAndUndefinedValues`) | partial | `ParsesAndRoundTripsAllBoundaryNumericAndDateCardForms` |
| Reserved and mandatory keywords are interpreted with required semantics | 4.4.1, 4.4.2, Appendix C | `Source/CFITS/Layout/DataLayout.cs`; `Source/CFITS/Header/FitsHeader.cs`; `Source/CFITS/Document/FitsDocumentBuilder.cs` | `Tools/CFITS.Tests/ImageSpecTests.cs`; `Tools/CFITS.Tests/TableSpecTests.cs` | partial | `RejectsMissingDuplicateOrMisorderedMandatoryKeywordsPerHduKind` |
| Big-endian data representation and supported BITPIX values | 5.1–5.3, Appendix E | `Source/CFITS/Data/Pixels.cs`; `Source/CFITS/Data/FitsImage.cs`; `Source/CFITS/Document/FitsDocumentBuilder.cs` | `Tools/CFITS.Tests/ImageSpecTests.cs` (`SupportsEveryImplementedBitpixAndWritesBigEndianData`) | partial | `DifferentiallyChecksAllBitpixBytePatternsIncludingNaNInfinityAndSignedBoundaries` |
| Image HDU dimensions, data size, scaling, and undefined integer pixels | 7.1.1–7.1.3; 5.4 | `Source/CFITS/Data/FitsImage.cs`; `Source/CFITS/Layout/DataLayout.cs`; `Source/CFITS/Reading/FitsReader.cs` | `Tools/CFITS.Tests/ImageSpecTests.cs` | covered | `RejectsImageHeaderWithOverflowingProductOrIllegalScalingCombination` |
| Random-groups keywords, parameter/data sequence, and scaling | 6.1–6.3 | `Source/CFITS/Data/FitsGroups.cs`; `Source/CFITS/Layout/DataLayout.cs`; `Source/CFITS/Document/FitsDocumentBuilder.cs` | `Tools/CFITS.Tests/RoundTrips/ConstructedRoundTripTests.cs`; `Tools/CFITS.Tests/Fixtures/OracleDump.cs` | partial | `ReadsAndRejectsRandomGroupsAgainstAstropyForMultipleParameterShapes` |
| ASCII-table mandatory keywords, field formats, rows, and entries | 7.2.1–7.2.5 | `Source/CFITS/Data/ColumnFormats.cs`; `Source/CFITS/Document/AsciiTableBuilder.cs`; `Source/CFITS/Data/FitsTable.cs` | `Tools/CFITS.Tests/TableSpecTests.cs`; `Tools/CFITS.Tests/RoundTrips/ConstructedRoundTripTests.cs` | partial | `RejectsIllegalAsciiFormatWidthPrecisionAndFieldOverlap` |
| Binary-table formats, row layout, vectors, complex values, bits, and nulls | 7.3.1–7.3.4 | `Source/CFITS/Data/ColumnFormats.cs`; `Source/CFITS/Document/BinaryTableBuilder.cs`; `Source/CFITS/Document/BinaryColumn.cs` | `Tools/CFITS.Tests/TableSpecTests.cs`; `Tools/CFITS.Tests/Data/FitsColumnBulkTests.cs` | partial | `DifferentiallyChecksEveryTFORMTypeAndRejectsInvalidRepeatAndWidth` |
| Binary-table variable-length arrays and heap offsets | 7.3.5, 7.3.6 | `Source/CFITS/Data/FitsColumn.cs`; `Source/CFITS/Document/BinaryTableBuilder.cs`; `Source/CFITS/Layout/DataLayout.cs` | `Tools/CFITS.Tests/TableSpecTests.cs` (`WritesVariableDescriptorsAndHeapOffsetsIndependently`) | partial | `RejectsVariableArrayDescriptorOutsideHeapAndTHEAPBounds` |
| WCS linear, celestial, spectral, and alternate-axis representations | 8.1–8.5 | `Source/CFITS/Core/FitsWorld.cs` | `Tools/CFITS.Tests/Coordinates/CoordinateTests.cs` | partial | `DifferentiallyChecksSupportedWcsAlgorithmsAndAlternateAxisDescriptionsAgainstAstropy` |
| Time-coordinate values, reference frames, scales, offsets, and units | 9.1–9.8 | `Source/CFITS/Core/FitsClock.cs` | `Tools/CFITS.Tests/Coordinates/CoordinateTests.cs` (`ClockConvertsReferenceAndUnits`) | partial | `RejectsInvalidTimeScaleReferencePositionAndTimeUnitAndChecksLeapSecondCases` |
| FITS exceptions identify malformed location/context without accepting truncation | 3.1–3.3, 4.4, 7.1–7.3 | `Source/CFITS/Core/FitsException.cs`; `Source/CFITS/Core/FitsLocation.cs`; `Source/CFITS/Reading/FitsReader.cs` | `Tools/CFITS.Tests/Validation/ValidationRegressionTests.cs`; header/image tests | partial | `ReportsHduKeywordAndByteOffsetForEachMalformedStructure` |

## Incorporated official conventions

The registry lists these six conventions as reviewed and incorporated into the
FITS 4.0 Standard. They are tracked separately because their detailed
requirements are distributed across the Standard and convention documents.

| Incorporated convention | Standard section | Current implementation path | Existing test path | Gap status | Proposed red test |
|---|---|---|---|---|---|
| Header space convention for reserving future header cards | 4.4.3; registry “Header space convention” | No dedicated reservation model or writer API found; ordinary header serialization is in `Source/CFITS/Header/CardImages.cs` and `Source/CFITS/Header/FitsHeaderBuilder.cs` | `Tools/CFITS.Tests/Headers/HeaderSpecTests.cs` covers ordinary padding only | missing | `PreservesReservedHeaderSpaceAndAllowsLaterKeywordInsertion` |
| CONTINUE long-string keyword convention | 4.2.1, 4.4.3; registry “CONTINUE Long String Keyword convention” | `Source/CFITS/Header/FitsCardView.cs`; `Source/CFITS/Header/FitsHeader.cs`; `Source/CFITS/Header/CardImages.cs` | `Tools/CFITS.Tests/Headers/HeaderSpecTests.cs` (`CombinesContinuedLongStringCards`) | partial | `WritesAndReadsLongStringsAtEveryContinuationBoundaryAndPreservesComments` |
| Column Limits keywords (`TLMINn`/`TLMAXn`, `TDMINn`/`TDMAXn`) | 4.4.2; registry “Column Limits keywords” | No dedicated handling found beyond generic header cards | No focused test found | missing | `ReadsAndValidatesColumnLimitsForScalarAndVectorColumns` |
| CHECKSUM and DATASUM integrity convention | 4.4.2.7, Appendix J; registry “CHECKSUM keyword convention” | `Source/CFITS/Core/FitsChecksum.cs`; `Source/CFITS/Core/FitsIntegrity.cs`; `Source/CFITS/Document/FitsDocument.cs` | `Tools/CFITS.Tests/Checksums/ChecksumTests.cs`; `Tools/CFITS.Tests/Validation/ValidationRegressionTests.cs` | partial | `RoundTripsReferenceChecksumsAndRejectsEveryInvalidHeaderDataAndDATASUMCombination` |
| Tiled image compression | 10.1–10.2, 10.4; registry “Tiled Image Compression” | `Source/CFITS/Compression/TileCompression.cs`; `Source/CFITS/Compression/Rice.cs`; `Source/CFITS/Compression/HCompress.cs`; `Source/CFITS/Document/FitsDocumentBuilder.cs` | `Tools/CFITS.Tests/Compression/CompressionTests.cs` | partial | `DifferentiallyChecksAllRequiredCompressionKeywordsTileEdgesAndQuantizationModes` |
| Tiled table compression | 10.3–10.4; registry “Tiled Table Compression” | `Source/CFITS/Compression/TableCompression.cs`; `Source/CFITS/Data/FitsTable.cs` | `Tools/CFITS.Tests/Compression/TableCompressionTests.cs` | partial | `ReadsAndValidatesCompressedTableDirectivesAlgorithmsAndVariableLengthColumns` |

## Highest-risk gaps

1. **Mandatory-keyword and structural validation** is broad but not yet
   demonstrated as a complete per-HDU-kind validator. Invalid combinations can
   therefore be accepted even when ordinary examples parse successfully.
2. **Binary-table edge coverage** is high risk because `TFORMn`, variable-length
   arrays, heap bounds, scaling, nulls, and multidimensional descriptors
   interact; the current tests cover representative builders rather than the
   full legal/illegal grammar.
3. **Compression interoperability** is primarily decoder coverage. The
   incorporated tiled-image and tiled-table conventions require more exhaustive
   keyword, tile-edge, algorithm, quantization, and malformed-input tests.
4. **WCS and time** have useful implementation and Astropy comparisons, but
   they are only partial slices of Sections 8 and 9 and do not establish full
   convention-level interoperability.
5. **Three incorporated conventions have no dedicated evidence**: header-space
   reservation, column limits, and (for CHECKSUM) complete invalid-state and
   write/read coverage.

## Audit findings addendum

This addendum records the completed focused audits. Priorities indicate the
risk of accepting or producing a file that is structurally invalid or
interoperable only with this implementation:

- **P0**: must be resolved before claiming FITS-compliant interchange.
- **P1**: important interoperability or standards-coverage gap, but not an
  immediate blocker for the currently exercised path.

| Priority | Audit finding and evidence | Standard section | Existing evidence | Proposed red test |
|---|---|---|---|---|
| P0 | **Header validation is permissive beyond the opening keyword.** `FitsReader` checks `SIMPLE` for the primary HDU and `XTENSION` for later HDUs, while the broader mandatory-keyword combinations are interpreted by `DataLayout`/`FitsHeader` rather than enforced as a complete per-HDU validation pass. | 3.3; 4.4.1–4.4.2; 7.1.1–7.3.2 | `Source/CFITS/Reading/FitsReader.cs`; `Source/CFITS/Layout/DataLayout.cs`; `Source/CFITS/Header/FitsHeader.cs`; `Tools/CFITS.Tests/Headers/HeaderSpecTests.cs`; `Tools/CFITS.Tests/ImageSpecTests.cs`; `Tools/CFITS.Tests/TableSpecTests.cs` | `RejectsMissingDuplicateMisorderedAndWrongTypeMandatoryKeywordsForEveryHduKind` |
| P0 | **Binary-table heap safety is not proven for hostile descriptors.** Variable-array builders and readers cover valid descriptors, but the audit found no focused boundary suite proving that descriptor counts, offsets, `PCOUNT`, `THEAP`, and row geometry cannot address outside the heap. | 7.3.1–7.3.6 | `Source/CFITS/Data/FitsColumn.cs`; `Source/CFITS/Data/FitsTable.cs`; `Source/CFITS/Layout/DataLayout.cs`; `Source/CFITS/Document/BinaryTableBuilder.cs`; `Tools/CFITS.Tests/TableSpecTests.cs` | `RejectsVariableArrayDescriptorsThatOverflowOrEscapeTheHeap` |
| P0 | **Compressed-image decoding is not complete convention coverage.** The implementation supports selected `RICE_1`, `GZIP_1`, `GZIP_2`, `PLIO_1`, and `HCOMPRESS_1` paths, but unsupported algorithms fail at runtime and the audit has not established all required keyword combinations, tile-edge rules, quantization, or undefined-pixel behavior. | 10.1.1–10.2.2; 10.4.1–10.4.4 | `Source/CFITS/Compression/TileCompression.cs`; `Source/CFITS/Compression/Rice.cs`; `Source/CFITS/Compression/HCompress.cs`; `Tools/CFITS.Tests/Compression/CompressionTests.cs` | `DifferentiallyChecksCompressedImagesAtTileEdgesWithEverySupportedAlgorithmAndQuantizationMode` |
| P0 | **Compressed-table interoperability is only partially audited.** The table-compression implementation and fixtures exercise representative inputs, but directive keywords, column replacement/restoration, algorithm selection, and variable-length columns need malformed and cross-tool coverage. | 10.3.1–10.3.6; 10.4 | `Source/CFITS/Compression/TableCompression.cs`; `Source/CFITS/Data/FitsTable.cs`; `Tools/CFITS.Tests/Compression/TableCompressionTests.cs` | `RoundTripsAstropyCompressedTablesAndRejectsInvalidCompressionDirectives` |
| P1 | **Header-space convention has no reservation or insertion API.** Header padding is emitted/preserved as ordinary block padding, but there is no evidence that reserved cards can be deliberately allocated and later filled without changing the HDU layout. | 4.4.3; incorporated Header Space convention | `Source/CFITS/Header/CardImages.cs`; `Source/CFITS/Header/FitsHeaderBuilder.cs`; `Tools/CFITS.Tests/Headers/HeaderSpecTests.cs` | `ReservesHeaderSpaceThenInsertsCardsWithoutMovingData` |
| P1 | **CONTINUE support is read-focused.** Continued strings are combined by the parser, but write-side boundary splitting, continuation comments, escaped quotes, and exact card geometry are not covered by the audit. | 4.2.1; 4.4.3; incorporated CONTINUE convention | `Source/CFITS/Header/FitsCardView.cs`; `Source/CFITS/Header/FitsHeader.cs`; `Source/CFITS/Header/CardImages.cs`; `Tools/CFITS.Tests/Headers/HeaderSpecTests.cs` | `WritesAndReadsLongStringsAcrossAllContinuationBoundaries` |
| P1 | **Column Limits convention is absent.** `TLMINn`/`TLMAXn` and `TDMINn`/`TDMAXn` remain generic cards with no column-level interpretation or validation. | 4.4.2; incorporated Column Limits convention | No dedicated implementation or focused test found; generic header paths are `Source/CFITS/Header/FitsHeader.cs` and `Source/CFITS/Header/FitsCardView.cs` | `ReadsColumnLimitsAndRejectsInvalidScalarVectorLimitDeclarations` |
| P1 | **CHECKSUM coverage verifies more than it writes.** `FitsChecksum` can stamp and inspect `CHECKSUM`/`DATASUM`, and open-time verification is optional, but complete reference-vector coverage for header placement, invalid states, compressed data, and write/read symmetry is absent. | 4.4.2.7; Appendix J; incorporated CHECKSUM convention | `Source/CFITS/Core/FitsChecksum.cs`; `Source/CFITS/Core/FitsIntegrity.cs`; `Source/CFITS/Document/FitsDocument.cs`; `Tools/CFITS.Tests/Checksums/ChecksumTests.cs` | `MatchesReferenceChecksumVectorsAndRejectsTamperedHeaderDataAndDatasum` |
| P1 | **Random-groups support is exercised narrowly.** The builder, layout, and accessors support representative groups, but required keyword combinations, zero-group/zero-parameter cases, scaling, and malformed group data need a focused conformance set. | 6.1–6.3 | `Source/CFITS/Data/FitsGroups.cs`; `Source/CFITS/Layout/DataLayout.cs`; `Source/CFITS/Document/FitsDocumentBuilder.cs`; `Tools/CFITS.Tests/RoundTrips/ConstructedRoundTripTests.cs`; `Tools/CFITS.Tests/Fixtures/OracleDump.cs` | `DifferentiallyChecksRandomGroupsKeywordRulesAndDataSequenceAgainstAstropy` |
| P1 | **WCS support is a tested subset, not full Section 8 coverage.** `FitsWorld` has Astropy comparisons for selected linear and projection cases, but alternate descriptions, axis metadata, spectral conventions, and invalid combinations are not comprehensively traced. | 8.1–8.5 | `Source/CFITS/Core/FitsWorld.cs`; `Tools/CFITS.Tests/Coordinates/CoordinateTests.cs` | `DifferentiallyChecksAlternateAxisCelestialAndSpectralWcsMetadataAgainstAstropy` |
| P1 | **Time support is a tested subset, not full Section 9 coverage.** `FitsClock` covers selected reference values and scales, but the audit lacks comprehensive validation for reference position/direction, leap-second behavior, time errors/binning, and global versus axis-specific overrides. | 9.1–9.8 | `Source/CFITS/Core/FitsClock.cs`; `Tools/CFITS.Tests/Coordinates/CoordinateTests.cs` | `ValidatesTimeFrameOverridesLeapSecondsUnitsAndBinnedTimeMetadata` |

These findings do not change the matrix statuses above; they make the
implementation and test evidence behind the highest-risk statuses explicit.

