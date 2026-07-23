namespace Transpiler.Engine;

/// <summary>The outcome of transpiling one document.</summary>
public sealed class TranspileResult
{
    public TranspileResult(string outputText, IReadOnlyList<Diagnostic> diagnostics, TimeSpan elapsed)
    {
        OutputText = outputText;
        Diagnostics = diagnostics;
        Elapsed = elapsed;
    }

    public string OutputText { get; }

    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    public TimeSpan Elapsed { get; }

    public bool Success => Diagnostics.All(static d => d.Severity != DiagnosticSeverity.Error);
}
