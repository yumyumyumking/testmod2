namespace Transpiler.Core.Syntax;

public sealed class IndexReference : Expression
{
    public IndexReference(string name, Expression index)
    {
        Name = name;
        Index = index;
    }

    public string Name { get; }

    public Expression Index { get; }
}
