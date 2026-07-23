namespace Transpiler.Core.Syntax;

public sealed class LabelStatement : Statement
{
    public LabelStatement(string name)
    {
        Name = name;
    }

    public string Name { get; }
}
