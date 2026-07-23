namespace Transpiler.Core.Syntax;

public sealed class NumberLiteral : Expression
{
    public NumberLiteral(string text)
    {
        Text = text;
    }

    /// <summary>The literal exactly as written (kept as text to avoid rounding drift).</summary>
    public string Text { get; }
}
