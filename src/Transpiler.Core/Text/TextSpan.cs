namespace Transpiler.Core.Text;

/// <summary>An absolute character range within a source document.</summary>
public readonly struct TextSpan
{
    public TextSpan(int start, int length)
    {
        Start = start;
        Length = length;
    }

    public int Start { get; }

    public int Length { get; }

    public int End => Start + Length;

    public static TextSpan FromBounds(int start, int end) => new(start, end - start);
}
