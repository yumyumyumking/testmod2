namespace Transpiler.Core.Diagnostics;

/// <summary>One reported problem, with its resolved source position.</summary>
public sealed class Diagnostic
{
    public Diagnostic(DiagnosticCode code, string message, TextSpan? span, LinePosition? position)
    {
        Code = code;
        Message = message;
        Span = span;
        Position = position;
    }

    public DiagnosticCode Code { get; }

    public string Message { get; }

    public TextSpan? Span { get; }

    /// <summary>1-based line/column, when the diagnostic maps to source.</summary>
    public LinePosition? Position { get; }

    public DiagnosticSeverity Severity => Code.Severity;

    public override string ToString()
    {
        var location = Position is { } p ? $" ({p.Line}:{p.Column})" : string.Empty;
        return $"{Code.Code} {Severity}{location}: {Message}";
    }
}
