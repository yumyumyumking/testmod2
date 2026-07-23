namespace Transpiler.Core.Syntax;

/// <summary>
/// The ONE place statement traversal is defined. Read-side: <see cref="Flatten"/> /
/// <see cref="ChildBodies"/> / <see cref="Expressions"/>, shared by the binder and the
/// transform passes. Write-side: <see cref="StatementRewriter"/>. Adding a statement
/// kind means extending this file (and the emitters/dumper, which are inherently
/// per-node) — passes built on these helpers pick the new kind up automatically.
/// </summary>
public static class StatementTree
{
    /// <summary>Depth-first enumeration of statements including all nested bodies.</summary>
    public static IEnumerable<Statement> Flatten(IReadOnlyList<Statement> statements)
    {
        foreach (var statement in statements)
        {
            yield return statement;
            foreach (var body in ChildBodies(statement))
            {
                foreach (var nested in Flatten(body))
                {
                    yield return nested;
                }
            }
        }
    }

    /// <summary>Every nested statement list of one statement (branches, bodies, handlers).</summary>
    public static IEnumerable<IReadOnlyList<Statement>> ChildBodies(Statement statement)
    {
        switch (statement)
        {
            case IfBlockStatement i:
                foreach (var branch in i.Branches)
                {
                    yield return branch.Body;
                }

                if (i.ElseBody is not null)
                {
                    yield return i.ElseBody;
                }

                break;
            case IfActionStatement ia:
                yield return ia.ElseAction is null
                    ? new[] { ia.ThenAction }
                    : new[] { ia.ThenAction, ia.ElseAction };
                break;
            case WhileStatement w:
                yield return w.Body;
                break;
            case RepeatStatement r:
                yield return r.Body;
                break;
            case TryStatement t:
                yield return t.Body;
                yield return t.Handler;
                break;
        }
    }

    /// <summary>
    /// The expressions a statement directly owns (targets, values, conditions, counts).
    /// Nested statements are NOT descended — combine with <see cref="Flatten"/> to see
    /// every expression in a region.
    /// </summary>
    public static IEnumerable<Expression> Expressions(Statement statement)
    {
        switch (statement)
        {
            case AssignmentStatement a:
                yield return a.Target;
                yield return a.Value;
                break;
            case SetStatement s:
                yield return s.Target;
                break;
            case IfActionStatement ia:
                yield return ia.Condition;
                break;
            case IfBlockStatement i:
                foreach (var branch in i.Branches)
                {
                    yield return branch.Condition;
                }

                break;
            case WhileStatement w:
                yield return w.Condition;
                break;
            case RepeatStatement r:
                yield return r.Count;
                break;
        }
    }
}
