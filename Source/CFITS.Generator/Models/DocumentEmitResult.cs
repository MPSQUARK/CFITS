using Microsoft.CodeAnalysis;

namespace CFITS.Generator.Models;

internal sealed class DocumentEmitResult
{
    public DocumentEmitResult(DocumentModel? model, List<Diagnostic> diagnostics)
    {
        Model = model;
        Diagnostics = diagnostics;
    }

    public DocumentModel? Model { get; }

    public List<Diagnostic> Diagnostics { get; }
}
