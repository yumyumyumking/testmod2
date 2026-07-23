namespace Transpiler.Core.Syntax;

/// <summary>Structured IF/ELSIF/ELSE (CLX).</summary>
public sealed class IfBlockStatement : Statement
{
    public IfBlockStatement(IReadOnlyList<IfBranch> branches, IReadOnlyList<Statement>? elseBody)
    {
        Branches = branches;
        ElseBody = elseBody;
    }

    /// <summary>First entry is the IF branch, the rest are ELSIF branches.</summary>
    public IReadOnlyList<IfBranch> Branches { get; }

    public IReadOnlyList<Statement>? ElseBody { get; }
}
