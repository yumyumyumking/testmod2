namespace Transpiler.Core.Syntax;

public sealed class GotoStatement : Statement
{
    public GotoStatement(string label)
    {
        Label = label;
    }

    public string Label { get; }
}
