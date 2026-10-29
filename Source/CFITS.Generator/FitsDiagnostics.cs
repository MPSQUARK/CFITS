using Microsoft.CodeAnalysis;

namespace CFITS.Generator;

internal static class FitsDiagnostics
{
    private const string Category = "CFITS";

    internal static readonly DiagnosticDescriptor NotPartial = Error(
        "CFITS0001",
        "FITS type must be partial",
        "'{0}' must be partial so CFITS can generate its implementation");

    internal static readonly DiagnosticDescriptor MixedHduIndexes = Error(
        "CFITS0002",
        "Mixed HDU indexes",
        "Declare every image and table HDU index, or omit every index so declaration order assigns them");

    internal static readonly DiagnosticDescriptor DuplicateHduIndex = Error(
        "CFITS0003",
        "Duplicate HDU index",
        "HDU index {0} is assigned more than once");

    internal static readonly DiagnosticDescriptor TableShape = Error(
        "CFITS0004",
        "Table model shape",
        "Table property '{0}' must be a model with exactly one IReadOnlyList<T> or List<T> row property");

    internal static readonly DiagnosticDescriptor ImageData = Error(
        "CFITS0005",
        "Image pixel data",
        "Image model '{0}' must declare exactly one [FitsData] property");

    internal static readonly DiagnosticDescriptor MissingOtherHdus = Error(
        "CFITS0006",
        "Unmapped HDUs",
        "'{0}' sets IgnoreOtherHdus to false and must declare an [FitsOtherHdus] property");

    internal static readonly DiagnosticDescriptor DuplicateMember = Error(
        "CFITS0007",
        "Duplicate document member",
        "'{0}' declares more than one {1}");

    internal static readonly DiagnosticDescriptor TwoKeywordObjects = Error(
        "CFITS0008",
        "Two keyword objects for one HDU",
        "HDU {0} has two typed keyword objects");

    internal static readonly DiagnosticDescriptor GeneratedNameClash = Error(
        "CFITS0009",
        "Generated property name",
        "'{0}' already declares '{1}', which CFITS would generate");

    internal static readonly DiagnosticDescriptor HeaderModelNotPartial = Error(
        "CFITS0010",
        "Header model must be partial",
        "'{0}' must be partial so CFITS can generate its {1} property");

    internal static readonly DiagnosticDescriptor OtherHduType = Error(
        "CFITS0011",
        "Other HDU property type",
        "[FitsOtherHdus] property '{0}' must be IReadOnlyList<FitsHduSnapshot>");

    internal static readonly DiagnosticDescriptor UnsupportedMember = Error(
        "CFITS0012",
        "Unsupported FITS member",
        "'{0}' is not a supported {1} member");

    internal static readonly DiagnosticDescriptor NotTopLevel = Error(
        "CFITS0013",
        "Nested FITS document",
        "'{0}' must be a top-level type");

    internal static readonly DiagnosticDescriptor RowColumns = Error(
        "CFITS0014",
        "Table columns",
        "Row type '{0}' must declare at least one [FitsColumn] property");

    internal static readonly DiagnosticDescriptor OtherHdusWhileIgnored = Error(
        "CFITS0020",
        "Other HDUs are ignored",
        "'{0}' declares [FitsOtherHdus] while IgnoreOtherHdus is true. Set IgnoreOtherHdus to false to keep unmapped HDUs");

    internal static readonly DiagnosticDescriptor InaccessibleSetter = Error(
        "CFITS0015",
        "Property setter",
        "'{0}' must have a public or internal setter");

    internal static readonly DiagnosticDescriptor NegativeHduIndex = Error(
        "CFITS0016",
        "Negative HDU index",
        "HDU index on '{0}' cannot be negative");

    internal static readonly DiagnosticDescriptor SpecialRecordsType = Error(
        "CFITS0017",
        "Special records property",
        "[FitsSpecialRecords] property '{0}' must be ReadOnlyMemory<byte> or byte[]");

    internal static readonly DiagnosticDescriptor StaticDocument = Error(
        "CFITS0018",
        "Static FITS document",
        "'{0}' cannot be static");

    internal static readonly DiagnosticDescriptor KeywordOnRow = Error(
        "CFITS0019",
        "Keyword on a table row",
        "Move [FitsKeyword] members of '{0}' onto the table model or a [FitsHeader] object");

    private static DiagnosticDescriptor Error(string id, string title, string message) =>
        new(id, title, message, Category, DiagnosticSeverity.Error, isEnabledByDefault: true);
}
