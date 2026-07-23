namespace Transpiler.Core.Syntax;

public sealed class NameReference : Expression
{
    public NameReference(string name)
    {
        Name = name;
    }

    public string Name { get; }
}
