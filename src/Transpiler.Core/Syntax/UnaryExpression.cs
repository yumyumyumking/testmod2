namespace Transpiler.Core.Syntax;

public sealed class UnaryExpression : Expression
{
    public UnaryExpression(UnaryOperator op, Expression operand)
    {
        Operator = op;
        Operand = operand;
    }

    public UnaryOperator Operator { get; }

    public Expression Operand { get; }
}
