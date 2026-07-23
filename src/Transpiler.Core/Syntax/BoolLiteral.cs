namespace Transpiler.Core.Syntax;

public sealed class BoolLiteral : Expression
{
    public BoolLiteral(bool value)
    {
        Value = value;
    }

    public bool Value { get; }
}
