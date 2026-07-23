namespace Transpiler.Desktop.ViewModels;

/// <summary>One row in the problems list; also drives squiggles and hover tooltips.</summary>
public sealed class DiagnosticItem
{
    public DiagnosticItem(Diagnostic diagnostic)
    {
        Code = diagnostic.Code.Code;
        Severity = diagnostic.Severity.ToString();
        IsError = diagnostic.Severity == DiagnosticSeverity.Error;
        Message = diagnostic.Message;
        Line = diagnostic.Position?.Line ?? 0;
        Column = diagnostic.Position?.Column ?? 0;
        Start = diagnostic.Span?.Start ?? -1;
        Length = diagnostic.Span?.Length ?? 0;
    }

    public string Code { get; }

    public string Severity { get; }

    public bool IsError { get; }

    public string Message { get; }

    public int Line { get; }

    public int Column { get; }

    /// <summary>Absolute character offset of the diagnostic span; -1 when unknown.</summary>
    public int Start { get; }

    public int Length { get; }

    public string Location => Line > 0 ? $"{Line}:{Column}" : "—";

    /// <summary>Single-line rendering for the IDE console, mirroring the diagnostic format
    /// (e.g. "CLX1003 Error (12:3): GOTO target 'LOOP_X' is not defined").</summary>
    public string Display =>
        $"{Code} {Severity}{(Line > 0 ? $" ({Line}:{Column})" : string.Empty)}: {Message}";

    public string Tooltip => $"{Code} ({Severity}): {Message}";
}
