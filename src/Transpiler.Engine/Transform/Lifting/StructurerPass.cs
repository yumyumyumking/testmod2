namespace Transpiler.Engine.Transform.Lifting;

/// <summary>
/// Pattern-directed structuring (SPEC §8.3): folds the flat GOTO/label idioms the
/// lowerer emits back into REPEAT, WHILE and IF/ELSIF/ELSE, iterating to fixpoint.
/// Matching keys on shape and label reference counts, not on label names, so
/// hand-written CL using the same idioms lifts too. (The verifier's read-back runs
/// with <see cref="PassContext.FoldGeneratedLabelsOnly"/>, restricting folds to
/// transpiler-generated labels so natively-written idioms are not re-shaped.)
/// A candidate region only folds when it is self-contained: no outside GOTO may
/// target a label an inner fold could consume. Whatever remains after fixpoint
/// stays as explicit labels and GOTOs — legal CLX — with an informational
/// diagnostic; retained labels and variables that carry the generated prefix are
/// renamed via <see cref="RetainedNameRenamer"/> so the emitted tree re-binds
/// cleanly. Idiom order per iteration is REPEAT → WHILE → IF/ELSE → IF.
///
/// One class, two files: this file (the pass — orchestration, the finishing sweep,
/// retained-name renaming), StructurerPass.Sweeps.cs (the four idiom recognizers
/// and their safety guards).
/// </summary>
public sealed partial class StructurerPass : IAstPass
{
    public string Name => "structurer";

    public ProgramSyntax Run(ProgramSyntax program, PassContext context)
    {
        var prefix = context.SourceLanguage.Labels.GeneratedPrefix;

        // Counters consumed by REPEAT folds are pass-local state: produced by
        // SweepRepeat, consumed by the declaration filter below, never crossing
        // a pass boundary.
        var removedCounters = new HashSet<string>(context.SourceLanguage.NameComparer);

        var result = program.MapSubRoutines(subRoutine =>
            subRoutine.WithBody(Structure(subRoutine.Body, subRoutine.Name, context, prefix, removedCounters)));

        // Drop declarations for folded-away REPEAT counters; rename any other
        // surviving generated-prefix declaration (an isGenerated flag would not
        // survive re-parsing, so the verifier's re-bind would report CLX2102).
        var declarations = new List<VariableDeclaration>();
        foreach (var declaration in result.Declarations)
        {
            if (!removedCounters.Contains(declaration.Name))
            {
                declarations.Add(declaration);
            }
        }

        var renames = new Dictionary<string, string>(context.SourceLanguage.NameComparer);
        if (!string.IsNullOrEmpty(prefix))
        {
            foreach (var declaration in declarations)
            {
                if (declaration.IsGenerated || !declaration.Name.StartsWith(prefix, context.NameComparison))
                {
                    continue;
                }

                var candidate = "RETAINED_" + declaration.Name[prefix.Length..];
                while (declarations.Any(d => string.Equals(d.Name, candidate, context.NameComparison)) ||
                       renames.Values.Any(taken => string.Equals(taken, candidate, context.NameComparison)))
                {
                    candidate += "_R";
                }

                renames[declaration.Name] = candidate;
            }
        }

        if (renames.Count > 0)
        {
            declarations = declarations
                .Select(d => renames.TryGetValue(d.Name, out var renamed)
                    ? new VariableDeclaration(d.Scope, renamed, d.Kind, d.Binding, d.IsGenerated)
                    {
                        Span = d.Span,
                        LeadingComments = d.LeadingComments,
                        TrailingComment = d.TrailingComment,
                    }
                    : d)
                .ToList();

            var renamer = RetainedNameRenamer.ForVariables(renames);
            result = result.MapSubRoutines(subRoutine => subRoutine.WithBody(renamer.RewriteList(subRoutine.Body)));
        }

        return result.WithDeclarations(declarations);
    }

    private List<Statement> Structure(
        IReadOnlyList<Statement> body,
        string routineName,
        PassContext context,
        string prefix,
        HashSet<string> removedCounters)
    {
        var list = Fold(new List<Statement>(body), context, prefix, removedCounters);
        var unstructured = false;
        list = Finish(list, ref unstructured);
        list = RenameRetainedGeneratedLabels(list, context, prefix);

        if (unstructured)
        {
            context.Diagnostics.Report(DiagnosticCodes.UnstructuredFlowRetained, null, routineName);
        }

        return list;
    }

    private List<Statement> Fold(List<Statement> list, PassContext context, string prefix, HashSet<string> removedCounters)
    {
        // Frames assembled by the mapping-lift pass (TRY bodies) are opaque to the
        // sweeps below; fold their bodies first so idioms inside frames structure too.
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] is TryStatement t)
            {
                list[i] = new TryStatement(
                    t.FaultVariable,
                    Fold(new List<Statement>(t.Body), context, prefix, removedCounters),
                    Fold(new List<Statement>(t.Handler), context, prefix, removedCounters))
                {
                    Span = t.Span,
                    LeadingComments = t.LeadingComments,
                    TrailingComment = t.TrailingComment,
                };
            }
        }

        var changed = true;
        while (changed)
        {
            changed =
                SweepRepeat(list, context, prefix, removedCounters) ||
                SweepWhile(list, context, prefix, removedCounters) ||
                SweepIfElse(list, context, prefix, removedCounters) ||
                SweepIfShort(list, context, prefix, removedCounters);
        }

        return list;
    }

    /// <summary>
    /// Recursive finishing sweep: remaining single-line IFs are canonicalized into
    /// IF blocks and finished recursively — nested inline IFs (IF a THEN … ELSE IF
    /// b THEN …) must normalize exactly like parsed blocks or round-trip dumps
    /// diverge. GOTOs and labels that survive mark unstructured flow.
    /// </summary>
    private static List<Statement> Finish(IReadOnlyList<Statement> body, ref bool unstructured)
    {
        var list = new List<Statement>(body.Count);
        foreach (var statement in body)
        {
            switch (statement)
            {
                case IfActionStatement action:
                    list.Add(FinishIfBlock(IfChains.Normalize(ToIfBlock(action)), ref unstructured));
                    break;

                case IfBlockStatement ifBlock:
                    list.Add(FinishIfBlock(IfChains.Normalize(ifBlock), ref unstructured));
                    break;

                case WhileStatement w:
                    list.Add(new WhileStatement(w.Condition, Finish(w.Body, ref unstructured))
                    {
                        Span = w.Span,
                        LeadingComments = w.LeadingComments,
                        TrailingComment = w.TrailingComment,
                    });
                    break;

                case RepeatStatement r:
                    list.Add(new RepeatStatement(r.Count, Finish(r.Body, ref unstructured))
                    {
                        Span = r.Span,
                        LeadingComments = r.LeadingComments,
                        TrailingComment = r.TrailingComment,
                    });
                    break;

                case TryStatement t:
                    list.Add(new TryStatement(t.FaultVariable, Finish(t.Body, ref unstructured), Finish(t.Handler, ref unstructured))
                    {
                        Span = t.Span,
                        LeadingComments = t.LeadingComments,
                        TrailingComment = t.TrailingComment,
                    });
                    break;

                case GotoStatement or LabelStatement:
                    unstructured = true;
                    list.Add(statement);
                    break;

                default:
                    list.Add(statement);
                    break;
            }
        }

        return list;
    }

    private static IfBlockStatement FinishIfBlock(IfBlockStatement normalized, ref bool unstructured)
    {
        var branches = new List<IfBranch>(normalized.Branches.Count);
        foreach (var branch in normalized.Branches)
        {
            branches.Add(new IfBranch(branch.Condition, Finish(branch.Body, ref unstructured)));
        }

        var elseBody = normalized.ElseBody is null ? null : Finish(normalized.ElseBody, ref unstructured);
        return new IfBlockStatement(branches, elseBody)
        {
            Span = normalized.Span,
            LeadingComments = normalized.LeadingComments,
            TrailingComment = normalized.TrailingComment,
        };
    }

    /// <summary>
    /// Retained labels carrying the generated prefix are renamed deterministically
    /// (with their GOTOs) so the lifted tree re-binds during verification.
    /// </summary>
    private static List<Statement> RenameRetainedGeneratedLabels(List<Statement> list, PassContext context, string prefix)
    {
        if (string.IsNullOrEmpty(prefix))
        {
            return list;
        }

        var allLabels = StatementTree.Flatten(list).OfType<LabelStatement>().Select(static l => l.Name).ToList();
        var map = new Dictionary<string, string>(context.SourceLanguage.NameComparer);
        foreach (var label in allLabels)
        {
            if (!label.StartsWith(prefix, context.NameComparison) || map.ContainsKey(label))
            {
                continue;
            }

            var candidate = "RETAINED_" + label[prefix.Length..];
            while (allLabels.Any(existing => string.Equals(existing, candidate, context.NameComparison)) ||
                   map.Values.Any(taken => string.Equals(taken, candidate, context.NameComparison)))
            {
                candidate += "_R";
            }

            map[label] = candidate;
        }

        if (map.Count == 0)
        {
            return list;
        }

        return RetainedNameRenamer.ForLabels(map).RewriteList(list).ToList();
    }

    private static IfBlockStatement ToIfBlock(IfActionStatement action)
    {
        IReadOnlyList<Statement>? elseBody = action.ElseAction is null ? null : new[] { action.ElseAction };
        return new IfBlockStatement(
            new[] { new IfBranch(action.Condition, new[] { action.ThenAction }) },
            elseBody)
        {
            Span = action.Span,
            LeadingComments = action.LeadingComments,
            TrailingComment = action.TrailingComment,
        };
    }
}
