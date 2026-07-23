namespace Transpiler.Core.Diagnostics;

/// <summary>
/// A catalogued diagnostic descriptor. The full catalogue lives in
/// <see cref="DiagnosticCodes"/>; messages are composite format templates.
/// </summary>
public sealed class DiagnosticCode
{
    public DiagnosticCode(string code, DiagnosticSeverity severity, string template)
    {
        Code = code;
        Severity = severity;
        Template = template;
    }

    public string Code { get; }

    public DiagnosticSeverity Severity { get; }

    public string Template { get; }
}
