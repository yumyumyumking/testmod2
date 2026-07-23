namespace Transpiler.Core.Syntax;

/// <summary>Target = value. In CL this is written with the SET keyword; in CLX it is bare.</summary>
public sealed class AssignmentStatement : Statement
{
    public AssignmentStatement(Expression target, Expression value)
    {
        Target = target;
        Value = value;
    }

    /// <summary>A <see cref="NameReference"/> or <see cref="IndexReference"/>.</summary>
    public Expression Target { get; }

    public Expression Value { get; }
}
