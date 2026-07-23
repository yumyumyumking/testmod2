namespace Transpiler.Engine.Transform.Lifting;

public sealed partial class StructurerPass
{
    // The four idiom recognizers, each one sweep over a statement list. A sweep
    // returns true after performing exactly one fold (the fixpoint loop in Fold
    // re-runs from the top), false when the idiom occurs nowhere in the list.

    // -------------------------------------------------------------- REPEAT idiom
    // i:   counter = 0                (counter has the generated prefix)
    // i+1: TOP:
    // i+2: IF counter >= count THEN GOTO END
    // ...  body
    // j:   counter = counter + 1
    // j+1: GOTO TOP
    // j+2: END:

    private bool SweepRepeat(List<Statement> list, PassContext context, string prefix, HashSet<string> removedCounters)
    {
        for (var i = 0; i + 5 < list.Count; i++)
        {
            if (list[i] is not AssignmentStatement { Target: NameReference counter, Value: NumberLiteral { Text: "0" } } init ||
                string.IsNullOrEmpty(prefix) ||
                !counter.Name.StartsWith(prefix, context.NameComparison))
            {
                continue;
            }

            if (list[i + 1] is not LabelStatement top ||
                list[i + 2] is not IfActionStatement
                {
                    ThenAction: GotoStatement endGoto,
                    ElseAction: null,
                    Condition: BinaryExpression { Operator: BinaryOperator.GreaterOrEqual, Left: NameReference guardVar } guard,
                } ||
                !SameName(guardVar.Name, counter.Name, context))
            {
                continue;
            }

            for (var j = i + 3; j + 2 < list.Count; j++)
            {
                if (list[j] is not AssignmentStatement
                    {
                        Target: NameReference incTarget,
                        Value: BinaryExpression
                        {
                            Operator: BinaryOperator.Add,
                            Left: NameReference incLeft,
                            Right: NumberLiteral { Text: "1" },
                        },
                    } ||
                    !SameName(incTarget.Name, counter.Name, context) ||
                    !SameName(incLeft.Name, counter.Name, context))
                {
                    continue;
                }

                if (list[j + 1] is not GotoStatement backGoto || !SameName(backGoto.Label, top.Name, context) ||
                    list[j + 2] is not LabelStatement end || !SameName(end.Name, endGoto.Label, context))
                {
                    continue;
                }

                if (!FoldAllowed(context, prefix, top.Name, end.Name))
                {
                    continue;
                }

                var refs = CountLabelReferences(list, context);
                if (Refs(refs, top.Name) != 1 || Refs(refs, end.Name) != 1)
                {
                    continue;
                }

                if (!RegionIsSelfContained(list, i + 3, j, context))
                {
                    continue;
                }

                // The counter ceases to exist after folding, so it may appear only
                // in the three idiom statements — a hand-written loop that also
                // reads its counter elsewhere must stay a GOTO loop.
                if (CounterUsedOutsideIdiom(list, counter.Name, i, j, context))
                {
                    continue;
                }

                var body = Fold(list.GetRange(i + 3, j - (i + 3)), context, prefix, removedCounters);
                var repeat = new RepeatStatement(guard.Right, body)
                {
                    Span = init.Span,
                    LeadingComments = init.LeadingComments,
                };

                removedCounters.Add(counter.Name);
                Replace(list, i, j + 3, repeat);
                return true;
            }
        }

        return false;
    }

    // --------------------------------------------------------------- WHILE idiom
    // i:   TOP:
    // i+1: IF inverted THEN GOTO END
    // ...  body
    // j:   GOTO TOP
    // j+1: END:

    private bool SweepWhile(List<Statement> list, PassContext context, string prefix, HashSet<string> removedCounters)
    {
        for (var i = 0; i + 3 < list.Count; i++)
        {
            if (list[i] is not LabelStatement top ||
                list[i + 1] is not IfActionStatement { ThenAction: GotoStatement endGoto, ElseAction: null } guard)
            {
                continue;
            }

            for (var j = i + 2; j + 1 < list.Count; j++)
            {
                if (list[j] is not GotoStatement backGoto || !SameName(backGoto.Label, top.Name, context) ||
                    list[j + 1] is not LabelStatement end || !SameName(end.Name, endGoto.Label, context))
                {
                    continue;
                }

                if (!FoldAllowed(context, prefix, top.Name, end.Name))
                {
                    continue;
                }

                var refs = CountLabelReferences(list, context);
                if (Refs(refs, top.Name) != 1 || Refs(refs, end.Name) != 1)
                {
                    continue;
                }

                if (!RegionIsSelfContained(list, i + 2, j, context))
                {
                    continue;
                }

                var body = Fold(list.GetRange(i + 2, j - (i + 2)), context, prefix, removedCounters);
                var loop = new WhileStatement(ExpressionOps.Invert(guard.Condition), body)
                {
                    Span = guard.Span,
                    LeadingComments = top.LeadingComments,
                };

                Replace(list, i, j + 2, loop);
                return true;
            }
        }

        return false;
    }

    // ------------------------------------------------------------- IF/ELSE idiom
    // i:   IF inverted THEN GOTO LELSE
    // ...  then-body
    // j:   GOTO LEND
    // j+1: LELSE:
    // ...  else-body
    // k:   LEND:

    private bool SweepIfElse(List<Statement> list, PassContext context, string prefix, HashSet<string> removedCounters)
    {
        for (var i = 0; i + 3 < list.Count; i++)
        {
            if (list[i] is not IfActionStatement { ThenAction: GotoStatement elseGoto, ElseAction: null } guard)
            {
                continue;
            }

            for (var j = i + 1; j + 1 < list.Count; j++)
            {
                if (list[j] is not GotoStatement endGoto ||
                    list[j + 1] is not LabelStatement elseLabel || !SameName(elseLabel.Name, elseGoto.Label, context))
                {
                    continue;
                }

                for (var k = j + 2; k < list.Count; k++)
                {
                    if (list[k] is not LabelStatement endLabel || !SameName(endLabel.Name, endGoto.Label, context))
                    {
                        continue;
                    }

                    if (!FoldAllowed(context, prefix, elseLabel.Name, endLabel.Name))
                    {
                        break;
                    }

                    var refs = CountLabelReferences(list, context);
                    if (Refs(refs, elseLabel.Name) != 1 || Refs(refs, endLabel.Name) != 1)
                    {
                        break;
                    }

                    if (!RegionIsSelfContained(list, i + 1, j, context) ||
                        !RegionIsSelfContained(list, j + 2, k, context))
                    {
                        break;
                    }

                    var thenBody = Fold(list.GetRange(i + 1, j - (i + 1)), context, prefix, removedCounters);
                    var elseBody = Fold(list.GetRange(j + 2, k - (j + 2)), context, prefix, removedCounters);
                    var ifBlock = new IfBlockStatement(
                        new[] { new IfBranch(ExpressionOps.Invert(guard.Condition), thenBody) },
                        elseBody)
                    {
                        Span = guard.Span,
                        LeadingComments = guard.LeadingComments,
                    };

                    Replace(list, i, k + 1, ifBlock);
                    return true;
                }

                break; // first GOTO/LELSE pairing is canonical; do not scan further
            }
        }

        return false;
    }

    // ---------------------------------------------------------- short IF idiom
    // i:   IF inverted THEN GOTO LEND
    // ...  then-body
    // k:   LEND:

    private bool SweepIfShort(List<Statement> list, PassContext context, string prefix, HashSet<string> removedCounters)
    {
        for (var i = 0; i + 1 < list.Count; i++)
        {
            if (list[i] is not IfActionStatement { ThenAction: GotoStatement endGoto, ElseAction: null } guard)
            {
                continue;
            }

            for (var k = i + 1; k < list.Count; k++)
            {
                if (list[k] is not LabelStatement endLabel || !SameName(endLabel.Name, endGoto.Label, context))
                {
                    continue;
                }

                if (!FoldAllowed(context, prefix, endLabel.Name))
                {
                    break;
                }

                var refs = CountLabelReferences(list, context);
                if (Refs(refs, endLabel.Name) != 1)
                {
                    break;
                }

                if (!RegionIsSelfContained(list, i + 1, k, context))
                {
                    break;
                }

                var thenBody = Fold(list.GetRange(i + 1, k - (i + 1)), context, prefix, removedCounters);
                var ifBlock = new IfBlockStatement(
                    new[] { new IfBranch(ExpressionOps.Invert(guard.Condition), thenBody) },
                    elseBody: null)
                {
                    Span = guard.Span,
                    LeadingComments = guard.LeadingComments,
                };

                Replace(list, i, k + 1, ifBlock);
                return true;
            }
        }

        return false;
    }

    // -------------------------------------------------------------- fold guards

    /// <summary>In restricted mode only transpiler-generated labels may fold.</summary>
    private static bool FoldAllowed(PassContext context, string prefix, params string[] labels)
    {
        if (!context.FoldGeneratedLabelsOnly)
        {
            return true;
        }

        if (string.IsNullOrEmpty(prefix))
        {
            return false;
        }

        foreach (var label in labels)
        {
            if (!label.StartsWith(prefix, context.NameComparison))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Guards a candidate region against label capture. The recursive fold of an
    /// extracted body computes reference counts over the slice alone, so a label
    /// that is referenced both inside the slice (slice-local count could hit the
    /// fold threshold of 1) and outside it (a reference the slice cannot see)
    /// could be consumed by an inner fold, stranding the outside GOTO. A label
    /// with no in-region references is safe — no sweep folds a 0-reference label —
    /// so a plain jump target inside a loop body does not block structuring.
    /// </summary>
    private static bool RegionIsSelfContained(List<Statement> list, int start, int endExclusive, PassContext context)
    {
        var region = list.GetRange(start, endExclusive - start);
        List<string>? insideLabels = null;
        foreach (var statement in StatementTree.Flatten(region))
        {
            if (statement is LabelStatement label)
            {
                (insideLabels ??= new List<string>()).Add(label.Name);
            }
        }

        if (insideLabels is null)
        {
            return true;
        }

        var insideRefs = CountLabelReferences(region, context);
        List<string>? capturable = null;
        foreach (var label in insideLabels)
        {
            if (Refs(insideRefs, label) > 0)
            {
                (capturable ??= new List<string>()).Add(label);
            }
        }

        if (capturable is null)
        {
            return true;
        }

        for (var i = 0; i < list.Count; i++)
        {
            if (i >= start && i < endExclusive)
            {
                continue;
            }

            foreach (var statement in StatementTree.Flatten(new[] { list[i] }))
            {
                if (statement is GotoStatement g &&
                    capturable.Any(name => string.Equals(name, g.Label, context.NameComparison)))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>True when the REPEAT counter is referenced by any statement other than the idiom's own three.</summary>
    private static bool CounterUsedOutsideIdiom(List<Statement> list, string counter, int init, int increment, PassContext context)
    {
        for (var index = 0; index < list.Count; index++)
        {
            if (index == init || index == init + 2 || index == increment)
            {
                continue;
            }

            foreach (var statement in StatementTree.Flatten(new[] { list[index] }))
            {
                foreach (var expression in StatementTree.Expressions(statement))
                {
                    if (ExpressionOps.ContainsName(expression, counter, context.NameComparison))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Reference counts for every label, over the whole (nested) region.
    /// <see cref="StatementTree.Flatten"/> yields IF-action branch statements as
    /// children, so counting bare <see cref="GotoStatement"/>s covers guard GOTOs
    /// exactly once.
    /// </summary>
    private static Dictionary<string, int> CountLabelReferences(List<Statement> list, PassContext context)
    {
        var refs = new Dictionary<string, int>(context.SourceLanguage.NameComparer);
        foreach (var statement in StatementTree.Flatten(list))
        {
            if (statement is GotoStatement g)
            {
                refs[g.Label] = Refs(refs, g.Label) + 1;
            }
        }

        return refs;
    }

    private static int Refs(Dictionary<string, int> refs, string label) =>
        refs.TryGetValue(label, out var count) ? count : 0;

    private static bool SameName(string a, string b, PassContext context) =>
        string.Equals(a, b, context.NameComparison);

    private static void Replace(List<Statement> list, int start, int endExclusive, Statement replacement)
    {
        list.RemoveRange(start, endExclusive - start);
        list.Insert(start, replacement);
    }
}
