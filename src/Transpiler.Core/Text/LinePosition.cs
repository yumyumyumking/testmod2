namespace Transpiler.Core.Text;

/// <summary>A 1-based line/column position.</summary>
public readonly struct LinePosition
{
    public LinePosition(int line, int column)
    {
        Line = line;
        Column = column;
    }

    public int Line { get; }

    public int Column { get; }
}
