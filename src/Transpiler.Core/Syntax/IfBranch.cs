namespace Transpiler.Core.Syntax;

/// <summary>One THEN-branch of a CLX IF block.</summary>
public sealed class IfBranch
{
    public IfBranch(Expression condition, IReadOnlyList<Statement> body)
    {
        Condition = condition;
        Body = body;
    }

    public Expression Condition { get; }

    public IReadOnlyList<Statement> Body { get; }
}
