namespace Transpiler.Core.Syntax;

public sealed class ArrayDeclarationStatement : Statement
{
    public ArrayDeclarationStatement(string name, string size)
    {
        Name = name;
        Size = size;
    }

    public string Name { get; }

    /// <summary>Size literal as written.</summary>
    public string Size { get; }
}
