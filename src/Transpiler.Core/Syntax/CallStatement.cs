namespace Transpiler.Core.Syntax;

public sealed class CallStatement : Statement
{
    public CallStatement(string name)
    {
        Name = name;
    }

    public string Name { get; }
}
