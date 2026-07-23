namespace Transpiler.Engine.Transform;

/// <summary>
/// Condition algebra shared by lowering and lifting. <see cref="Invert"/> is an
/// involution (Invert(Invert(c)) is structurally c) so the lifter can recover the
/// original condition from the lowered inverted guard (SPEC §8.2/§8.3).
/// </summary>
public static class ExpressionOps
{
    public static Expression Invert(Expression condition) => condition switch
    {
        UnaryExpression { Operator: UnaryOperator.Not } negated => negated.Operand,
        BoolLiteral b => new BoolLiteral(!b.Value),
        BinaryExpression { Operator: BinaryOperator.Equal } b => new BinaryExpression(BinaryOperator.NotEqual, b.Left, b.Right),
        BinaryExpression { Operator: BinaryOperator.NotEqual } b => new BinaryExpression(BinaryOperator.Equal, b.Left, b.Right),
        BinaryExpression { Operator: BinaryOperator.Less } b => new BinaryExpression(BinaryOperator.GreaterOrEqual, b.Left, b.Right),
        BinaryExpression { Operator: BinaryOperator.GreaterOrEqual } b => new BinaryExpression(BinaryOperator.Less, b.Left, b.Right),
        BinaryExpression { Operator: BinaryOperator.Greater } b => new BinaryExpression(BinaryOperator.LessOrEqual, b.Left, b.Right),
        BinaryExpression { Operator: BinaryOperator.LessOrEqual } b => new BinaryExpression(BinaryOperator.Greater, b.Left, b.Right),
        _ => new UnaryExpression(UnaryOperator.Not, condition),
    };

    /// <summary>True when the expression uses AND/OR/NOT composition.</summary>
    public static bool IsComplexCondition(Expression e) => e switch
    {
        UnaryExpression { Operator: UnaryOperator.Not } u => true,
        BinaryExpression { Operator: BinaryOperator.And or BinaryOperator.Or } => true,
        _ => false,
    };

    /// <summary>Depth-first search for any <see cref="IndexReference"/> in an expression.</summary>
    public static bool ContainsIndexReference(Expression e) => e switch
    {
        IndexReference => true,
        UnaryExpression u => ContainsIndexReference(u.Operand),
        BinaryExpression b => ContainsIndexReference(b.Left) || ContainsIndexReference(b.Right),
        _ => false,
    };

    /// <summary>Depth-first search for a reference to <paramref name="name"/> (scalar or indexed).</summary>
    public static bool ContainsName(Expression e, string name, StringComparison comparison) => e switch
    {
        NameReference n => string.Equals(n.Name, name, comparison),
        IndexReference i => string.Equals(i.Name, name, comparison) || ContainsName(i.Index, name, comparison),
        UnaryExpression u => ContainsName(u.Operand, name, comparison),
        BinaryExpression b => ContainsName(b.Left, name, comparison) || ContainsName(b.Right, name, comparison),
        _ => false,
    };
}
