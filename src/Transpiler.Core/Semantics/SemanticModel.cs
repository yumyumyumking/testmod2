namespace Transpiler.Core.Semantics;

/// <summary>Symbols and semantic diagnostics for one program.</summary>
public sealed class SemanticModel
{
    public SemanticModel(
        IReadOnlyDictionary<string, Symbol> fileScope,
        IReadOnlyList<Diagnostic> diagnostics)
    {
        FileScope = fileScope;
        Diagnostics = diagnostics;
    }

    /// <summary>Variables and arrays, keyed per language comparer.</summary>
    public IReadOnlyDictionary<string, Symbol> FileScope { get; }

    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    public bool HasErrors => Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error);
}
