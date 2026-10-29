using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace CFITS.Generator;

[Generator]
public sealed partial class FitsContextGenerator : IIncrementalGenerator
{
    private const string NewLine = "\n";
    // Bulk copies amortize their buffers and setup only on larger tables.
    private const int BulkReadRowThreshold = 8_192;

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var documents = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => IsDocumentDeclaration(node),
            static (syntax, _) => Describe(syntax));
        context.RegisterSourceOutput(documents.Collect(), static (output, results) => EmitAll(output, results));
    }

    private static void EmitAll(SourceProductionContext output, ImmutableArray<DocumentEmitResult?> results)
    {
        var headers = new HashSet<string>(StringComparer.Ordinal);
        foreach (DocumentEmitResult? result in results)
        {
            if (result is null)
            {
                continue;
            }

            foreach (Diagnostic diagnostic in result.Diagnostics)
            {
                output.ReportDiagnostic(diagnostic);
            }

            if (result.Model is null)
            {
                continue;
            }

            output.AddSource(
                SourceHint(result.Model.Symbol) + ".Document.g.cs",
                SourceText.From(EmitDocument(result.Model), Encoding.UTF8));
            foreach (INamedTypeSymbol target in result.Model.HeaderTargets)
            {
                if (!headers.Add(target.ToDisplayString()))
                {
                    continue;
                }

                output.AddSource(
                    SourceHint(target) + ".Header.g.cs",
                    SourceText.From(EmitHeaderCompanion(target), Encoding.UTF8));
            }
        }
    }

    private static string EmitHeaderCompanion(INamedTypeSymbol symbol)
    {
        string space = NamespaceOf(symbol);
        string initializer = symbol.IsReferenceType ? " = null!;" : "";
        return $$"""
            #nullable enable
            {{space}}{{AccessibilityOf(symbol)}} partial {{KindOf(symbol)}} {{symbol.Name}}
            {
                public CFITS.FitsHeader Header { get; internal set; }{{initializer}}
            }
            """;
    }

    private static string NamespaceOf(INamedTypeSymbol symbol) =>
        symbol.ContainingNamespace.IsGlobalNamespace
            ? ""
            : $"namespace {symbol.ContainingNamespace.ToDisplayString()};{NewLine}{NewLine}";

    private static string SourceHint(INamedTypeSymbol symbol)
    {
        var hint = new StringBuilder();
        foreach (char character in symbol.ToDisplayString())
        {
            hint.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        return hint.ToString();
    }
}
