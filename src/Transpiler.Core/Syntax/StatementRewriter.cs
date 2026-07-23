namespace Transpiler.Core.Syntax;

/// <summary>
/// Identity-preserving tree rewriter: override only the node kinds you change and the
/// rest of the tree is rebuilt (or, when nothing changed, returned by reference —
/// callers can cheaply detect no-ops). Every rebuild carries Span and comments, so a
/// rewrite never loses source fidelity. Statement kinds without children or names
/// (CALL, RETURN, ARRAY, markers, skipped lines) pass through untouched by default
/// but still have hooks.
/// </summary>
public abstract class StatementRewriter
{
    /// <summary>Rewrites a statement list, preserving the original instance when nothing changed.</summary>
    public IReadOnlyList<Statement> RewriteList(IReadOnlyList<Statement> statements)
    {
        List<Statement>? changed = null;
        for (var i = 0; i < statements.Count; i++)
        {
            var rewritten = Rewrite(statements[i]);
            if (changed is null && !ReferenceEquals(rewritten, statements[i]))
            {
                changed = new List<Statement>(statements.Count);
                for (var j = 0; j < i; j++)
                {
                    changed.Add(statements[j]);
                }
            }

            changed?.Add(rewritten);
        }

        return changed ?? statements;
    }

    public virtual Statement Rewrite(Statement statement) => statement switch
    {
        AssignmentStatement a => RewriteAssignment(a),
        SetStatement s => RewriteSet(s),
        IfActionStatement ia => RewriteIfAction(ia),
        IfBlockStatement b => RewriteIfBlock(b),
        WhileStatement w => RewriteWhile(w),
        RepeatStatement r => RewriteRepeat(r),
        TryStatement t => RewriteTry(t),
        GotoStatement g => RewriteGoto(g),
        LabelStatement l => RewriteLabel(l),
        CallStatement c => RewriteCall(c),
        ReturnStatement ret => RewriteReturn(ret),
        ArrayDeclarationStatement ad => RewriteArrayDeclaration(ad),
        MarkerStatement m => RewriteMarker(m),
        SkippedStatement sk => RewriteSkipped(sk),
        _ => statement,
    };

    protected virtual Statement RewriteAssignment(AssignmentStatement node)
    {
        var target = RewriteExpression(node.Target);
        var value = RewriteExpression(node.Value);
        if (ReferenceEquals(target, node.Target) && ReferenceEquals(value, node.Value))
        {
            return node;
        }

        return new AssignmentStatement(target, value)
        {
            Span = node.Span, LeadingComments = node.LeadingComments, TrailingComment = node.TrailingComment,
        };
    }

    protected virtual Statement RewriteSet(SetStatement node)
    {
        var target = RewriteExpression(node.Target);
        if (ReferenceEquals(target, node.Target))
        {
            return node;
        }

        return new SetStatement(node.Value, target)
        {
            Span = node.Span, LeadingComments = node.LeadingComments, TrailingComment = node.TrailingComment,
        };
    }

    protected virtual Statement RewriteIfAction(IfActionStatement node)
    {
        var condition = RewriteExpression(node.Condition);
        var thenAction = Rewrite(node.ThenAction);
        var elseAction = node.ElseAction is null ? null : Rewrite(node.ElseAction);
        if (ReferenceEquals(condition, node.Condition) &&
            ReferenceEquals(thenAction, node.ThenAction) &&
            ReferenceEquals(elseAction, node.ElseAction))
        {
            return node;
        }

        return new IfActionStatement(condition, thenAction, elseAction)
        {
            Span = node.Span, LeadingComments = node.LeadingComments, TrailingComment = node.TrailingComment,
        };
    }

    protected virtual Statement RewriteIfBlock(IfBlockStatement node)
    {
        List<IfBranch>? branches = null;
        for (var i = 0; i < node.Branches.Count; i++)
        {
            var branch = node.Branches[i];
            var condition = RewriteExpression(branch.Condition);
            var body = RewriteList(branch.Body);
            var same = ReferenceEquals(condition, branch.Condition) && ReferenceEquals(body, branch.Body);
            if (branches is null && !same)
            {
                branches = new List<IfBranch>(node.Branches.Count);
                for (var j = 0; j < i; j++)
                {
                    branches.Add(node.Branches[j]);
                }
            }

            branches?.Add(same ? branch : new IfBranch(condition, body));
        }

        var elseBody = node.ElseBody is null ? null : RewriteList(node.ElseBody);
        if (branches is null && ReferenceEquals(elseBody, node.ElseBody))
        {
            return node;
        }

        return new IfBlockStatement(branches ?? (IReadOnlyList<IfBranch>)node.Branches, elseBody)
        {
            Span = node.Span, LeadingComments = node.LeadingComments, TrailingComment = node.TrailingComment,
        };
    }

    protected virtual Statement RewriteWhile(WhileStatement node)
    {
        var condition = RewriteExpression(node.Condition);
        var body = RewriteList(node.Body);
        if (ReferenceEquals(condition, node.Condition) && ReferenceEquals(body, node.Body))
        {
            return node;
        }

        return new WhileStatement(condition, body)
        {
            Span = node.Span, LeadingComments = node.LeadingComments, TrailingComment = node.TrailingComment,
        };
    }

    protected virtual Statement RewriteRepeat(RepeatStatement node)
    {
        var count = RewriteExpression(node.Count);
        var body = RewriteList(node.Body);
        if (ReferenceEquals(count, node.Count) && ReferenceEquals(body, node.Body))
        {
            return node;
        }

        return new RepeatStatement(count, body)
        {
            Span = node.Span, LeadingComments = node.LeadingComments, TrailingComment = node.TrailingComment,
        };
    }

    protected virtual Statement RewriteTry(TryStatement node)
    {
        var body = RewriteList(node.Body);
        var handler = RewriteList(node.Handler);
        if (ReferenceEquals(body, node.Body) && ReferenceEquals(handler, node.Handler))
        {
            return node;
        }

        return new TryStatement(node.FaultVariable, body, handler)
        {
            Span = node.Span, LeadingComments = node.LeadingComments, TrailingComment = node.TrailingComment,
        };
    }

    protected virtual Statement RewriteGoto(GotoStatement node) => node;

    protected virtual Statement RewriteLabel(LabelStatement node) => node;

    protected virtual Statement RewriteCall(CallStatement node) => node;

    protected virtual Statement RewriteReturn(ReturnStatement node) => node;

    protected virtual Statement RewriteArrayDeclaration(ArrayDeclarationStatement node) => node;

    protected virtual Statement RewriteMarker(MarkerStatement node) => node;

    protected virtual Statement RewriteSkipped(SkippedStatement node) => node;

    protected virtual Expression RewriteExpression(Expression expression) => expression switch
    {
        NameReference n => RewriteName(n),
        IndexReference i => RewriteIndex(i),
        UnaryExpression u => RewriteUnary(u),
        BinaryExpression b => RewriteBinary(b),
        _ => expression,
    };

    protected virtual Expression RewriteName(NameReference node) => node;

    protected virtual Expression RewriteIndex(IndexReference node)
    {
        var index = RewriteExpression(node.Index);
        return ReferenceEquals(index, node.Index) ? node : new IndexReference(node.Name, index);
    }

    protected virtual Expression RewriteUnary(UnaryExpression node)
    {
        var operand = RewriteExpression(node.Operand);
        return ReferenceEquals(operand, node.Operand) ? node : new UnaryExpression(node.Operator, operand);
    }

    protected virtual Expression RewriteBinary(BinaryExpression node)
    {
        var left = RewriteExpression(node.Left);
        var right = RewriteExpression(node.Right);
        return ReferenceEquals(left, node.Left) && ReferenceEquals(right, node.Right)
            ? node
            : new BinaryExpression(node.Operator, left, right);
    }
}
