namespace Transpiler.Core.Syntax;

public sealed class BinaryExpression : Expression
{
    public BinaryExpression(BinaryOperator op, Expression left, Expression right)
    {
        Operator = op;
        Left = left;
        Right = right;
    }

    public BinaryOperator Operator { get; }

    public Expression Left { get; }

    public Expression Right { get; }
}
