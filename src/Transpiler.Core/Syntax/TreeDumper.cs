using System.Text;

namespace Transpiler.Core.Syntax;

/// <summary>
/// Produces a canonical, comment- and span-insensitive S-expression for a tree.
/// Used for structural equality in the round-trip verifier (SPEC §8.4) and in tests.
/// IF shapes are canonicalized through <see cref="IfChains.Normalize"/> at dump time,
/// so nested-else and elsif spellings compare equal.
/// </summary>
public static class TreeDumper
{
    public static string Dump(ProgramSyntax program)
    {
        var sb = new StringBuilder();
        sb.Append("(program");

        if (program.Header is { } header)
        {
            sb.Append(" (hdr ").Append(Norm(header.Name));
            DumpExtras(sb, header.Fields);
            sb.Append(')');
        }

        // Bindings are intentionally excluded: CLX declares unbound locals and the
        // transpiler allocates points, so the round trip compares modulo allocation.
        foreach (var decl in program.Declarations)
        {
            sb.Append(" (decl ").Append(decl.Scope == VariableScopeKind.External ? "ext " : "loc ")
              .Append(Norm(decl.Name)).Append(' ')
              .Append(KindTag(decl.EffectiveKind)).Append(')');
        }

        foreach (var mainRoutine in program.MainRoutines)
        {
            sb.Append(" (main ").Append(Norm(mainRoutine.Name));
            DumpExtras(sb, mainRoutine.ExtraCaptures);
            foreach (var subRoutine in mainRoutine.SubRoutines)
            {
                sb.Append(" (sub ").Append(Norm(subRoutine.Name));
                DumpExtras(sb, subRoutine.ExtraCaptures);
                DumpStatements(sb, subRoutine.Body);
                sb.Append(')');
            }

            sb.Append(')');
        }

        foreach (var routine in program.FileRoutines)
        {
            sb.Append(" (").Append(routine.Kind.ToLowerInvariant()).Append(' ').Append(Norm(routine.Name));
            DumpExtras(sb, routine.ExtraCaptures);
            DumpStatements(sb, routine.Body);
            sb.Append(')');
        }

        sb.Append(')');
        return sb.ToString();
    }

    /// <summary>
    /// Extra recipe-pattern captures, sorted by key so dictionary order never affects
    /// structural comparison. Values are compared verbatim: they round-trip through
    /// the emit template character-for-character.
    /// </summary>
    private static void DumpExtras(StringBuilder sb, IReadOnlyDictionary<string, string> extras)
    {
        if (extras.Count == 0)
        {
            return;
        }

        foreach (var pair in extras.OrderBy(static p => p.Key, StringComparer.Ordinal))
        {
            sb.Append(" (x ").Append(pair.Key).Append('=').Append(pair.Value).Append(')');
        }
    }

    private static void DumpStatements(StringBuilder sb, IReadOnlyList<Statement> statements)
    {
        foreach (var statement in statements)
        {
            sb.Append(' ');
            DumpStatement(sb, statement);
        }
    }

    private static void DumpStatement(StringBuilder sb, Statement statement)
    {
        switch (statement)
        {
            case AssignmentStatement a:
                sb.Append("(= ");
                DumpExpression(sb, a.Target);
                sb.Append(' ');
                DumpExpression(sb, a.Value);
                sb.Append(')');
                break;
            case SetStatement s:
                // A flag write is equivalent to assigning the boolean literal (SET x == x = ON),
                // so dump it in that canonical form. This lets a language that expresses flags
                // as 'x = true' (MATLAB) round-trip structurally against SET/RESET.
                sb.Append("(= ");
                DumpExpression(sb, s.Target);
                sb.Append(s.Value ? " #t)" : " #f)");
                break;
            case IfBlockStatement i:
                DumpIf(sb, IfChains.Normalize(i));
                break;
            case IfActionStatement ia:
                sb.Append("(ifact ");
                DumpExpression(sb, ia.Condition);
                sb.Append(' ');
                DumpStatement(sb, ia.ThenAction);
                if (ia.ElseAction is not null)
                {
                    sb.Append(' ');
                    DumpStatement(sb, ia.ElseAction);
                }

                sb.Append(')');
                break;
            case WhileStatement w:
                sb.Append("(while ");
                DumpExpression(sb, w.Condition);
                DumpStatements(sb, w.Body);
                sb.Append(')');
                break;
            case RepeatStatement r:
                sb.Append("(repeat ");
                DumpExpression(sb, r.Count);
                DumpStatements(sb, r.Body);
                sb.Append(')');
                break;
            case TryStatement t:
                sb.Append("(try ").Append(Norm(t.FaultVariable));
                DumpStatements(sb, t.Body);
                sb.Append(" (catch");
                DumpStatements(sb, t.Handler);
                sb.Append("))");
                break;
            case ArrayDeclarationStatement ad:
                sb.Append("(array ").Append(Norm(ad.Name)).Append(' ').Append(ad.Size).Append(')');
                break;
            case GotoStatement g:
                sb.Append("(goto ").Append(Norm(g.Label)).Append(')');
                break;
            case LabelStatement l:
                sb.Append("(label ").Append(Norm(l.Name)).Append(')');
                break;
            case CallStatement c:
                sb.Append("(call ").Append(Norm(c.Name)).Append(')');
                break;
            case ReturnStatement:
                sb.Append("(return)");
                break;
            case MarkerStatement m:
                sb.Append("(marker ").Append(m.RuleName).Append('/').Append(m.MappingId).Append(')');
                break;
            case SkippedStatement sk:
                sb.Append("(skipped ").Append(sk.RawText).Append(')');
                break;
        }
    }

    private static void DumpIf(StringBuilder sb, IfBlockStatement node)
    {
        sb.Append("(if");
        foreach (var branch in node.Branches)
        {
            sb.Append(" (br ");
            DumpExpression(sb, branch.Condition);
            DumpStatements(sb, branch.Body);
            sb.Append(')');
        }

        if (node.ElseBody is not null)
        {
            sb.Append(" (else");
            DumpStatements(sb, node.ElseBody);
            sb.Append(')');
        }

        sb.Append(')');
    }

    private static void DumpExpression(StringBuilder sb, Expression expression)
    {
        switch (expression)
        {
            case NumberLiteral n:
                sb.Append(n.Text);
                break;
            case StringLiteral s:
                sb.Append(s.RawText);
                break;
            case BoolLiteral b:
                sb.Append(b.Value ? "#t" : "#f");
                break;
            case NameReference name:
                sb.Append(Norm(name.Name));
                break;
            case IndexReference index:
                sb.Append("(idx ").Append(Norm(index.Name)).Append(' ');
                DumpExpression(sb, index.Index);
                sb.Append(')');
                break;
            case UnaryExpression u:
                sb.Append(u.Operator == UnaryOperator.Not ? "(not " : "(neg ");
                DumpExpression(sb, u.Operand);
                sb.Append(')');
                break;
            case BinaryExpression bin:
                sb.Append('(').Append(OpName(bin.Operator)).Append(' ');
                DumpExpression(sb, bin.Left);
                sb.Append(' ');
                DumpExpression(sb, bin.Right);
                sb.Append(')');
                break;
        }
    }

    private static string OpName(BinaryOperator op) => op switch
    {
        BinaryOperator.Or => "or",
        BinaryOperator.And => "and",
        BinaryOperator.Equal => "==",
        BinaryOperator.NotEqual => "!=",
        BinaryOperator.Less => "<",
        BinaryOperator.LessOrEqual => "<=",
        BinaryOperator.Greater => ">",
        BinaryOperator.GreaterOrEqual => ">=",
        BinaryOperator.Add => "+",
        BinaryOperator.Subtract => "-",
        BinaryOperator.Multiply => "*",
        BinaryOperator.Divide => "/",
        _ => "?",
    };

    /// <summary>Identifiers compare case-insensitively (default language behavior).</summary>
    private static string Norm(string name) => name.ToUpperInvariant();

    /// <summary>Canonical kind tags (stable dump strings; "nn"/"fl" predate the byte kind).</summary>
    private static string KindTag(VariableKind kind) => kind switch
    {
        VariableKind.Boolean => "fl",
        VariableKind.Byte => "by",
        _ => "nn",
    };
}
