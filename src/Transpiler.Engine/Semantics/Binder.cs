namespace Transpiler.Engine.Semantics;

/// <summary>
/// Builds the symbol table and runs the semantic checks of SPEC §10 (CLX1xxx):
/// declaration/use consistency, per-subRoutine label scoping, array/scalar usage,
/// generated-prefix collisions, CALL resolution against this file's routines
/// (with never-called-function and recursion-cycle checks), and the
/// returnInMain capability. Labels are scoped to their STEP; a GOTO may only
/// target a label in the same subRoutine.
/// </summary>
public sealed class Binder
{
    private readonly DiagnosticBag _diagnostics;
    private readonly Dictionary<string, Symbol> _fileScope;
    private readonly StringComparer _comparer;

    private Binder(SyntaxTree tree, LanguageProfile language, DiagnosticBag? diagnostics)
    {
        _diagnostics = diagnostics ?? new DiagnosticBag(tree.Text);
        _comparer = language.NameComparer;
        _fileScope = new Dictionary<string, Symbol>(_comparer);
    }

    /// <summary>
    /// Binds the tree and runs the semantic checks. When a <paramref name="diagnostics"/>
    /// sink is supplied, findings are reported into it (the compilation-wide sink);
    /// otherwise a private bag is used and returned on the <see cref="SemanticModel"/>.
    /// </summary>
    public static SemanticModel Bind(SyntaxTree tree, LanguageProfile language, DiagnosticBag? diagnostics = null)
    {
        var binder = new Binder(tree, language, diagnostics);
        return binder.Run(tree, language);
    }

    private SemanticModel Run(SyntaxTree tree, LanguageProfile language)
    {
        var program = tree.Root;
        var prefix = language.Labels.GeneratedPrefix;

        // File scope: declarations and arrays. The generated-prefix check only applies
        // to CLX sources: lowered CL legitimately contains generated counter
        // declarations (REPEAT), which must re-bind cleanly.
        foreach (var declaration in program.Declarations)
        {
            if (language.Capabilities.BlockIf &&
                !declaration.IsGenerated &&
                NameHasGeneratedPrefix(declaration.Name, prefix))
            {
                _diagnostics.Report(DiagnosticCodes.GeneratedPrefixCollision, declaration.Span, declaration.Name, prefix);
            }

            var kind = declaration.Scope == VariableScopeKind.External
                ? SymbolKind.ExternalVariable
                : SymbolKind.LocalVariable;
            if (!TryDeclare(declaration.Name, new Symbol(kind, declaration.Name, declaration.Kind)))
            {
                _diagnostics.Report(DiagnosticCodes.DuplicateDeclaration, declaration.Span, declaration.Name);
            }
        }

        foreach (var subRoutine in AllRoutines(program))
        {
            CollectArrays(subRoutine.Body);
        }

        foreach (var mainRoutine in program.MainRoutines)
        {
            foreach (var subRoutine in mainRoutine.SubRoutines)
            {
                BindRoutine(subRoutine, language, prefix, isMainRoutine: true);
            }
        }

        foreach (var routine in program.FileRoutines)
        {
            BindRoutine(routine, language, prefix, isMainRoutine: false);
        }

        BindCallGraph(program);

        return new SemanticModel(_fileScope, _diagnostics.ToList());
    }

    /// <summary>
    /// CALL resolution and call-graph checks: CLX1004 for calls no routine in this
    /// file answers to (a warning — targets may live in another controller file),
    /// CLX1005 for Function routines nothing calls (functions exist to be called;
    /// sub-routines run with the scan and handlers fire on events, so neither is flagged),
    /// and CLX1008 for recursive call cycles.
    /// </summary>
    private void BindCallGraph(ProgramSyntax program)
    {
        var routines = new Dictionary<string, SubRoutine>(_comparer);
        foreach (var subRoutine in AllRoutines(program))
        {
            routines.TryAdd(subRoutine.Name, subRoutine);
        }

        var called = new HashSet<string>(_comparer);
        var edges = new Dictionary<string, List<(string Target, Statement Site)>>(_comparer);
        foreach (var subRoutine in AllRoutines(program))
        {
            foreach (var statement in StatementTree.Flatten(subRoutine.Body))
            {
                if (statement is not CallStatement call)
                {
                    continue;
                }

                called.Add(call.Name);
                if (!routines.ContainsKey(call.Name))
                {
                    _diagnostics.Report(DiagnosticCodes.CallTargetNotFound, call.Span, call.Name);
                }

                if (!edges.TryGetValue(subRoutine.Name, out var targets))
                {
                    edges[subRoutine.Name] = targets = new List<(string, Statement)>();
                }

                targets.Add((call.Name, statement));
            }
        }

        foreach (var routine in program.FileRoutines)
        {
            if (routine.Kind == SectionSlots.Function && !called.Contains(routine.Name))
            {
                _diagnostics.Report(DiagnosticCodes.SubroutineNeverCalled, routine.Span, routine.Name);
            }
        }

        DetectCallCycle(routines, edges);
    }

    private void DetectCallCycle(
        Dictionary<string, SubRoutine> routines,
        Dictionary<string, List<(string Target, Statement Site)>> edges)
    {
        // Colors: 0 unvisited, 1 on the current path, 2 finished. One report suffices.
        var state = new Dictionary<string, int>(_comparer);
        var path = new List<string>();

        bool Visit(string name)
        {
            state[name] = 1;
            path.Add(name);
            if (edges.TryGetValue(name, out var targets))
            {
                foreach (var (target, site) in targets)
                {
                    if (!routines.ContainsKey(target))
                    {
                        continue; // unresolved calls were already reported
                    }

                    var color = state.TryGetValue(target, out var value) ? value : 0;
                    if (color == 1)
                    {
                        var start = path.FindIndex(entry => _comparer.Equals(entry, target));
                        var cycle = string.Join(" -> ", path.Skip(start).Append(target));
                        _diagnostics.Report(DiagnosticCodes.RecursiveCall, site.Span, cycle);
                        return true;
                    }

                    if (color == 0 && Visit(target))
                    {
                        return true;
                    }
                }
            }

            path.RemoveAt(path.Count - 1);
            state[name] = 2;
            return false;
        }

        foreach (var name in routines.Keys)
        {
            if ((state.TryGetValue(name, out var color) ? color : 0) == 0 && Visit(name))
            {
                return;
            }
        }
    }

    private static IEnumerable<SubRoutine> AllRoutines(ProgramSyntax program) =>
        program.MainRoutines.SelectMany(static mainRoutine => mainRoutine.SubRoutines).Concat(program.FileRoutines);

    private bool TryDeclare(string name, Symbol symbol)
    {
        if (_fileScope.ContainsKey(name))
        {
            return false;
        }

        _fileScope[name] = symbol;
        return true;
    }

    private void CollectArrays(IReadOnlyList<Statement> statements)
    {
        foreach (var statement in statements)
        {
            if (statement is ArrayDeclarationStatement array)
            {
                if (!TryDeclare(array.Name, new Symbol(SymbolKind.Array, array.Name)))
                {
                    _diagnostics.Report(DiagnosticCodes.DuplicateDeclaration, array.Span, array.Name);
                }
            }

            foreach (var child in StatementTree.ChildBodies(statement))
            {
                CollectArrays(child);
            }
        }
    }

    private void BindRoutine(SubRoutine subRoutine, LanguageProfile language, string prefix, bool isMainRoutine)
    {
        var returnForbidden = isMainRoutine &&
            string.Equals(language.Capabilities.ReturnInMain, "forbidden", StringComparison.OrdinalIgnoreCase);

        // Pass A: labels (scoped to this sub-routine). Duplicates are ambiguous GOTO
        // targets regardless of spelling, so the check has no prefix exemption.
        var labels = new HashSet<string>(_comparer);
        var labelSites = new Dictionary<string, TextSpan>(_comparer);
        var targeted = new HashSet<string>(_comparer);
        foreach (var statement in StatementTree.Flatten(subRoutine.Body))
        {
            if (statement is LabelStatement label)
            {
                if (!labels.Add(label.Name))
                {
                    _diagnostics.Report(DiagnosticCodes.DuplicateLabel, label.Span, label.Name);
                }
                else
                {
                    labelSites[label.Name] = label.Span;
                }

                if (language.Capabilities.BlockIf && NameHasGeneratedPrefix(label.Name, prefix))
                {
                    _diagnostics.Report(DiagnosticCodes.GeneratedPrefixCollision, label.Span, label.Name, prefix);
                }
            }
        }

        // Pass B: uses.
        foreach (var statement in StatementTree.Flatten(subRoutine.Body))
        {
            switch (statement)
            {
                case GotoStatement g:
                    targeted.Add(g.Label);
                    if (!labels.Contains(g.Label))
                    {
                        _diagnostics.Report(DiagnosticCodes.GotoTargetNotFound, g.Span, g.Label);
                    }
                    else if (language.Capabilities.BlockIf)
                    {
                        _diagnostics.Report(DiagnosticCodes.UnstructuredJump, g.Span, g.Label);
                    }

                    break;
                case AssignmentStatement a:
                    BindExpression(a.Target, a.Span, isTarget: true);
                    BindExpression(a.Value, a.Span, isTarget: false);
                    break;
                case SetStatement s:
                    BindExpression(s.Target, s.Span, isTarget: true);
                    break;
                case IfActionStatement ia:
                    BindExpression(ia.Condition, ia.Span, isTarget: false);
                    break;
                case IfBlockStatement i:
                    foreach (var branch in i.Branches)
                    {
                        BindExpression(branch.Condition, statement.Span, isTarget: false);
                    }

                    break;
                case WhileStatement w:
                    BindExpression(w.Condition, w.Span, isTarget: false);
                    break;
                case RepeatStatement rp:
                    BindExpression(rp.Count, rp.Span, isTarget: false);
                    break;
                case ReturnStatement ret when returnForbidden:
                    _diagnostics.Report(DiagnosticCodes.ReturnInMain, ret.Span);
                    break;
            }
        }

        foreach (var label in labels)
        {
            if (!targeted.Contains(label) && !NameHasGeneratedPrefix(label, prefix))
            {
                var site = labelSites.TryGetValue(label, out var span) ? span : subRoutine.Span;
                _diagnostics.Report(DiagnosticCodes.LabelNeverTargeted, site, label);
            }
        }
    }

    private void BindExpression(Expression expression, TextSpan span, bool isTarget)
    {
        switch (expression)
        {
            case NameReference name:
                if (!_fileScope.TryGetValue(name.Name, out var symbol))
                {
                    _diagnostics.Report(DiagnosticCodes.UndeclaredVariable, span, name.Name);
                }
                else if (symbol.Kind == SymbolKind.Array)
                {
                    // Arrays have no scalar value in either position — reading one
                    // bare would otherwise surface only as a late verify failure.
                    _diagnostics.Report(DiagnosticCodes.IndexingMismatch, span, name.Name, "an array", "as a scalar");
                }

                break;
            case IndexReference index:
                if (!_fileScope.TryGetValue(index.Name, out var arraySymbol))
                {
                    _diagnostics.Report(DiagnosticCodes.UndeclaredVariable, span, index.Name);
                }
                else if (arraySymbol.Kind is not SymbolKind.Array)
                {
                    _diagnostics.Report(DiagnosticCodes.IndexingMismatch, span, index.Name, "a scalar", "with an index");
                }

                BindExpression(index.Index, span, isTarget: false);
                break;
            case UnaryExpression unary:
                BindExpression(unary.Operand, span, isTarget: false);
                break;
            case BinaryExpression binary:
                BindExpression(binary.Left, span, isTarget: false);
                BindExpression(binary.Right, span, isTarget: false);
                break;
        }
    }

    private static bool NameHasGeneratedPrefix(string name, string prefix) =>
        !string.IsNullOrEmpty(prefix) && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
}
