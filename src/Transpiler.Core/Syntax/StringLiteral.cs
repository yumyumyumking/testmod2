namespace Transpiler.Core.Syntax;

public sealed class StringLiteral : Expression
{
    public StringLiteral(string rawText)
    {
        RawText = rawText;
    }

    /// <summary>Includes the surrounding quotes.</summary>
    public string RawText { get; }
}
