namespace Transpiler.Core.Syntax;

/// <summary>
/// One lexical token. Keywords are not distinguished at the lexical level — the
/// parser matches identifier text against the active language's keyword table, which
/// is what makes keyword spellings a pure configuration concern.
/// </summary>
public sealed class Token
{
    public Token(TokenKind kind, string text, TextSpan span, string? comment = null)
    {
        Kind = kind;
        Text = text;
        Span = span;
        Comment = comment;
    }

    public TokenKind Kind { get; }

    public string Text { get; }

    public TextSpan Span { get; }

    /// <summary>
    /// For <see cref="TokenKind.EndOfLine"/>/<see cref="TokenKind.EndOfFile"/> tokens:
    /// the comment text (marker stripped, trimmed) that appeared on the terminated line.
    /// </summary>
    public string? Comment { get; }
}
