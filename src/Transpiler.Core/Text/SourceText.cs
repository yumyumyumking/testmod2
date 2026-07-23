namespace Transpiler.Core.Text;

/// <summary>
/// Immutable source document with line mapping. All spans produced by the lexer and
/// parser index into this text.
/// </summary>
public sealed class SourceText
{
    private readonly string _text;
    private readonly int[] _lineStarts;

    private SourceText(string text)
    {
        _text = text;
        _lineStarts = ComputeLineStarts(text);
    }

    public int Length => _text.Length;

    public char this[int index] => _text[index];

    public static SourceText From(string text) => new(text ?? string.Empty);

    public string ToString(TextSpan span) => _text.Substring(span.Start, span.Length);

    /// <summary>1-based line/column for an absolute position.</summary>
    public LinePosition GetLinePosition(int position)
    {
        var line = Array.BinarySearch(_lineStarts, position);
        if (line < 0)
        {
            line = ~line - 1;
        }

        return new LinePosition(line + 1, position - _lineStarts[line] + 1);
    }

    private static int[] ComputeLineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                starts.Add(i + 1);
            }
        }

        return starts.ToArray();
    }
}
