namespace Transpiler.Core.Syntax;

/// <summary>Bare SET (raise) / RESET (clear) of a point.</summary>
public sealed class SetStatement : Statement
{
    public SetStatement(bool value, Expression target)
    {
        Value = value;
        Target = target;
    }

    /// <summary>true = SET, false = RESET.</summary>
    public bool Value { get; }

    public Expression Target { get; }
}
