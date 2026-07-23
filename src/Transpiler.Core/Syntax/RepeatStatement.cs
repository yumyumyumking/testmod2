namespace Transpiler.Core.Syntax;

public sealed class RepeatStatement : Statement
{
    public RepeatStatement(Expression count, IReadOnlyList<Statement> body)
    {
        Count = count;
        Body = body;
    }

    public Expression Count { get; }

    public IReadOnlyList<Statement> Body { get; }
}
