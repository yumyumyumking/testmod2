namespace Transpiler.Core.Syntax;

/// <summary>A malformed line kept for error recovery; carries a diagnostic.</summary>
public sealed class SkippedStatement : Statement
{
    public SkippedStatement(string rawText)
    {
        RawText = rawText;
    }

    public string RawText { get; }
}
