namespace Transpiler.Core.Syntax;

public sealed class WhileStatement : Statement
{
    public WhileStatement(Expression condition, IReadOnlyList<Statement> body)
    {
        Condition = condition;
        Body = body;
    }

    public Expression Condition { get; }

    public IReadOnlyList<Statement> Body { get; }
}
