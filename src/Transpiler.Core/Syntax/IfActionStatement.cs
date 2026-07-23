namespace Transpiler.Core.Syntax;

/// <summary>CL single-line IF: one action per branch, no blocks.</summary>
public sealed class IfActionStatement : Statement
{
    public IfActionStatement(Expression condition, Statement thenAction, Statement? elseAction)
    {
        Condition = condition;
        ThenAction = thenAction;
        ElseAction = elseAction;
    }

    public Expression Condition { get; }

    public Statement ThenAction { get; }

    public Statement? ElseAction { get; }
}
