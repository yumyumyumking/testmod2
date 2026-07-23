using System.Text;

namespace Transpiler.Engine.Emit;

/// <summary>
/// Pretty printers for both languages (SPEC §6.1 back end). Output is deterministic
/// and comment-preserving: leading comments print as full-line comments above their
/// statement, trailing comments at end of line. Emitted text always re-parses — the
/// verifier enforces this on every run.
/// </summary>
public sealed class Emitter
{
    private readonly LanguageProfile _language;
    private readonly FormattingProfile _format;
    private readonly DiagnosticBag _diagnostics;
    private readonly SectionPlan _plan;
    private readonly VariablePlan _variables;
    private readonly SectionRule? _mainRule;
    private readonly SectionRule? _subRule;
    private readonly StringBuilder _sb = new();
    private int _indent;

    private Emitter(LanguageProfile language, FormattingProfile format, DiagnosticBag diagnostics)
    {
        _language = language;
        _format = format;
        _diagnostics = diagnostics;
        _plan = language.Plan;
        _variables = language.Variables;
        _mainRule = _plan.FindRule(SectionContent.MainRoutine);
        _subRule = _plan.FindRule(SectionContent.SubRoutine);
    }

    public static string Emit(ProgramSyntax program, LanguageProfile language, FormattingProfile format, DiagnosticBag diagnostics)
    {
        var emitter = new Emitter(language, format, diagnostics);
        emitter.EmitProgram(program);
        return emitter._sb.ToString();
    }

    private KeywordTable Kw => _language.Keywords;

    private BlockStyle Blocks => _language.Blocks;

    private bool BraceBlocks => _language.Blocks.Style == BlockDelimiterStyle.Braces;

    // ------------------------------------------------------------ section lines
    // Every section line is written from its plan rule: a pattern-mode rule renders
    // its emit template from the node's fields, filling placeholders the node never
    // carried from the section's authored defaults; a keyword-mode rule writes the
    // plain "START Name" shape. A terminator without an endEmit template is written
    // bare — recipes that want "END {name}" say so in endEmit.

    private static string HeaderLineOf(SectionRule ns, ProgramHeader header) => ns.EmitTemplate is { } template
        ? SectionPatterns.Render(template, WithName(header.Fields, header.Name, ns.Defaults))
        : $"{ns.Delimiters.Start} {header.Name}";

    private static string SectionHeadLineOf(SectionRule rule, string name, IReadOnlyDictionary<string, string> extras) =>
        rule.EmitTemplate is { } template
            ? SectionPatterns.Render(template, WithName(extras, name, rule.Defaults))
            : $"{rule.Delimiters.Start} {name}";

    private static string TerminatorLineOf(SectionRule rule, string name, IReadOnlyDictionary<string, string> extras) =>
        rule.EndEmitTemplate is { } template
            ? SectionPatterns.Render(template, WithName(extras, name, rule.Defaults))
            : rule.Delimiters.End!;

    /// <summary>
    /// The render map for a section line: the rule's <c>defaults</c> first, the
    /// node's own fields over them, and the well-known {name} on top — the emitted
    /// program's values always win; defaults only fill what it never carried.
    /// </summary>
    private static IReadOnlyDictionary<string, string> WithName(
        IReadOnlyDictionary<string, string> fields,
        string name,
        IReadOnlyDictionary<string, string> defaults)
    {
        var map = new Dictionary<string, string>(defaults, StringComparer.Ordinal);
        foreach (var (key, value) in fields)
        {
            map[key] = value;
        }

        map[SectionPatterns.NameCapture] = name;
        return map;
    }

    private void EmitProgram(ProgramSyntax program)
    {
        if (program.Header is { } header)
        {
            if (_plan.Namespace is { } ns)
            {
                EmitComments(header.LeadingComments);
                Line(HeaderLineOf(ns, header), header.TrailingComment);
                _sb.AppendLine();
            }
            else
            {
                // The target language has no file-header section; the header is dropped.
                _diagnostics.Report(DiagnosticCodes.HeaderNotRepresentable, header.Span, _language.Name);
            }
        }
        else if (_plan.Namespace is { } requiredNs)
        {
            // The source language carries no file header (e.g. MATLAB) but the target
            // requires one — synthesize a generic SEQUENCE so the output is valid. The
            // round-trip verifier compares modulo this synthesized header.
            Line(HeaderLineOf(requiredNs, ProgramHeader.Synthetic), null);
            _sb.AppendLine();
        }

        foreach (var declaration in program.Declarations)
        {
            EmitComments(declaration.LeadingComments);
            EmitDeclaration(declaration);
        }

        if (program.Declarations.Count > 0)
        {
            _sb.AppendLine();
        }

        EmitRoutines(program);
    }

    /// <summary>
    /// One declaration under the target's variable model: the scope entry chosen for
    /// the declaration's engine scope and bound/unbound state supplies the keyword; a
    /// kind annotation is written only when the target declares that kind; a binding
    /// is written only where the chosen scope's policy admits one (a forbidden
    /// binding is dropped — the verifier compares modulo bindings by design). A scope
    /// the target has no entry for is unrepresentable: CLX2302.
    /// </summary>
    private void EmitDeclaration(VariableDeclaration declaration)
    {
        var bound = declaration.Binding is not null;
        var scope = _variables.ScopeForEmit(declaration.Scope, bound);
        if (scope is null)
        {
            _diagnostics.Report(DiagnosticCodes.CapabilityMissing, declaration.Span,
                $"{declaration.Scope} declaration '{declaration.Name}'",
                $"variables.scopes ({declaration.Scope})");
            return;
        }

        var text = $"{scope.Keyword} {declaration.Name}";
        if (declaration.Kind is { } kind && _variables.KindRuleFor(kind) is { } kindRule)
        {
            text += $" : {kindRule.Spelling}";
        }

        if (declaration.Binding is { } binding && scope.Binding != BindingPolicy.Forbidden)
        {
            text += $" !{binding.Area}.{binding.PointType}({binding.Index})";
        }

        Line(text, declaration.TrailingComment);
    }

    private void EmitRoutines(ProgramSyntax program)
    {
        var firstMain = true;
        foreach (var mainRoutine in program.MainRoutines)
        {
            if (!firstMain)
            {
                _sb.AppendLine();
            }

            firstMain = false;
            EmitComments(mainRoutine.LeadingComments);
            if (_mainRule is { } mainRule)
            {
                Line(SectionHeadLineOf(mainRule, mainRoutine.Name, mainRoutine.ExtraCaptures), mainRoutine.TrailingComment);
            }

            foreach (var subRoutine in mainRoutine.SubRoutines)
            {
                EmitRoutine(subRoutine, _subRule);
            }

            if (_mainRule is { HasTerminator: true } closingMain)
            {
                Line(TerminatorLineOf(closingMain, mainRoutine.Name, mainRoutine.ExtraCaptures), null);
            }
        }

        foreach (var routine in program.FileRoutines)
        {
            var rule = RuleForKind(routine.Kind);
            if (rule is null)
            {
                // A file-level routine cannot be flattened away — callers reference it.
                _diagnostics.Report(DiagnosticCodes.SectionNotRepresentable, routine.Span,
                    _language.Name, routine.Kind, routine.Name);
                continue;
            }

            _sb.AppendLine();
            EmitRoutine(routine, rule);
        }

        if (program.Header is { } closingHeader && _plan.Namespace is { HasTerminator: true } closingNs)
        {
            Line(TerminatorLineOf(closingNs, closingHeader.Name, closingHeader.Fields), null);
        }
    }

    /// <summary>The plan rule a routine's <see cref="SectionSlots"/> tag maps onto; null when unrepresentable.</summary>
    private SectionRule? RuleForKind(string kind) => kind switch
    {
        SectionSlots.Function => _plan.FindRule(SectionContent.Function),
        SectionSlots.Handler => _plan.FindRule(SectionContent.Handler),
        _ => _subRule,
    };

    /// <summary>
    /// One routine section. A missing sub-routine rule (no such section in this
    /// language) emits the body inline — the enclosing routine absorbs it.
    /// </summary>
    private void EmitRoutine(SubRoutine subRoutine, SectionRule? rule)
    {
        EmitComments(subRoutine.LeadingComments);
        if (rule is null)
        {
            EmitStatements(subRoutine.Body);
            return;
        }

        if (BraceBlocks && !rule.UsesPatterns)
        {
            Line($"{rule.Delimiters.Start} {subRoutine.Name} {Blocks.Open}", subRoutine.TrailingComment);
            _indent++;
            EmitStatements(subRoutine.Body);
            _indent--;
            Line(Blocks.Close, null);
        }
        else
        {
            Line(SectionHeadLineOf(rule, subRoutine.Name, subRoutine.ExtraCaptures), subRoutine.TrailingComment);
            _indent++;
            EmitStatements(subRoutine.Body);
            _indent--;
            if (rule.HasTerminator)
            {
                Line(TerminatorLineOf(rule, subRoutine.Name, subRoutine.ExtraCaptures), null);
            }
        }
    }

    private void EmitStatements(IReadOnlyList<Statement> statements)
    {
        foreach (var statement in statements)
        {
            EmitStatement(statement);
        }
    }

    private void EmitStatement(Statement statement)
    {
        EmitComments(statement.LeadingComments);

        switch (statement)
        {
            case AssignmentStatement a:
            {
                var target = ExpressionWriter.Write(a.Target, _language);
                var value = ExpressionWriter.Write(a.Value, _language);
                Line(!_language.Capabilities.BlockIf
                    ? $"{Kw.Set} {target} = {value}"
                    : $"{target} = {value}", statement.TrailingComment);
                break;
            }

            case SetStatement s:
                Line(_language.Capabilities.SetReset
                    ? $"{(s.Value ? Kw.Set : Kw.Reset)} {ExpressionWriter.Write(s.Target, _language)}"
                    : $"{ExpressionWriter.Write(s.Target, _language)} = {(s.Value ? Kw.BoolTrue : Kw.BoolFalse)}",
                    statement.TrailingComment);
                break;

            case IfActionStatement action:
            {
                var text = $"{Kw.If} {ExpressionWriter.Write(action.Condition, _language)} {Kw.Then} {InlineAction(action.ThenAction)}";
                if (action.ElseAction is not null)
                {
                    text += $" {Kw.Else} {InlineAction(action.ElseAction)}";
                }

                Line(text, statement.TrailingComment);
                break;
            }

            case IfBlockStatement i when _language.Capabilities.BlockIf:
                EmitIfBlock(i);
                break;

            case WhileStatement w when _language.Capabilities.BlockIf:
            {
                var condition = ExpressionWriter.Write(w.Condition, _language);
                var head = _language.Capabilities.RequiresDo ? $"{Kw.While} {condition} {Kw.Do}" : $"{Kw.While} {condition}";
                Line(BraceBlocks ? $"{head} {Blocks.Open}" : head, statement.TrailingComment);
                _indent++;
                EmitStatements(w.Body);
                _indent--;
                Line(BraceBlocks ? Blocks.Close : Kw.EndWhile, null);
                break;
            }

            case RepeatStatement r when _language.Capabilities.BlockIf:
            {
                var head = $"{Kw.Repeat} {ExpressionWriter.Write(r.Count, _language)} {Kw.Times}";
                Line(BraceBlocks ? $"{head} {Blocks.Open}" : head, statement.TrailingComment);
                _indent++;
                EmitStatements(r.Body);
                _indent--;
                Line(BraceBlocks ? Blocks.Close : Kw.EndRepeat, null);
                break;
            }

            case TryStatement t when _language.Capabilities.BlockIf:
                Line(BraceBlocks ? $"{Kw.Try} {Blocks.Open}" : Kw.Try, statement.TrailingComment);
                _indent++;
                EmitStatements(t.Body);
                _indent--;
                var catchLine = _language.Capabilities.CatchWithParens
                    ? $"{Kw.Catch} ({t.FaultVariable})"
                    : $"{Kw.Catch} {t.FaultVariable}";
                Line(BraceBlocks
                    ? $"{Blocks.Close} {Kw.Catch} ({t.FaultVariable}) {Blocks.Open}"
                    : catchLine, null);
                _indent++;
                EmitStatements(t.Handler);
                _indent--;
                Line(BraceBlocks ? Blocks.Close : Kw.EndTry, null);
                break;

            case ArrayDeclarationStatement array when _language.Capabilities.BlockIf:
                Line($"{Kw.Array} {array.Name}[{array.Size}]", statement.TrailingComment);
                break;

            case GotoStatement g:
                Line($"{Kw.Goto} {g.Label}", statement.TrailingComment);
                break;

            case LabelStatement label:
            {
                var text = label.Name + _language.Labels.Suffix;
                if (_format.LabelsFlushLeft)
                {
                    AppendComment(_sb.Append(text), label.TrailingComment);
                    _sb.AppendLine();
                }
                else
                {
                    Line(text, label.TrailingComment);
                }

                break;
            }

            case CallStatement call:
                Line(_language.Capabilities.ImplicitCall ? $"{call.Name}()" : $"{Kw.Call} {call.Name}", statement.TrailingComment);
                break;

            case ReturnStatement:
                Line(Kw.Return, statement.TrailingComment);
                break;

            case MarkerStatement marker:
                Line(marker.Text, statement.TrailingComment);
                break;

            case SkippedStatement skipped:
                Line(skipped.RawText, statement.TrailingComment);
                break;

            default:
                // A structured construct reached a CL emitter — lowering failed.
                _diagnostics.Report(DiagnosticCodes.UnloweredConstruct, statement.Span, statement.GetType().Name);
                break;
        }
    }

    private void EmitIfBlock(IfBlockStatement node)
    {
        if (BraceBlocks)
        {
            for (var i = 0; i < node.Branches.Count; i++)
            {
                var branch = node.Branches[i];
                var head = $"{(i == 0 ? Kw.If : Kw.ElseIf)} {ExpressionWriter.Write(branch.Condition, _language)} {Kw.Then} {Blocks.Open}";
                Line(i == 0 ? head : $"{Blocks.Close} {head}", i == 0 ? node.TrailingComment : null);
                _indent++;
                EmitStatements(branch.Body);
                _indent--;
            }

            if (node.ElseBody is not null)
            {
                Line($"{Blocks.Close} {Kw.Else} {Blocks.Open}", null);
                _indent++;
                EmitStatements(node.ElseBody);
                _indent--;
            }

            Line(Blocks.Close, null);
            return;
        }

        for (var i = 0; i < node.Branches.Count; i++)
        {
            var branch = node.Branches[i];
            var keyword = i == 0 ? Kw.If : Kw.ElseIf;
            var condition = ExpressionWriter.Write(branch.Condition, _language);
            var head = _language.Capabilities.RequiresThen ? $"{keyword} {condition} {Kw.Then}" : $"{keyword} {condition}";
            Line(head, i == 0 ? node.TrailingComment : null);
            _indent++;
            EmitStatements(branch.Body);
            _indent--;
        }

        if (node.ElseBody is not null)
        {
            Line(Kw.Else, null);
            _indent++;
            EmitStatements(node.ElseBody);
            _indent--;
        }

        Line(Kw.EndIf, null);
    }

    private string InlineAction(Statement action) => action switch
    {
        SetStatement s => $"{(s.Value ? Kw.Set : Kw.Reset)} {ExpressionWriter.Write(s.Target, _language)}",
        AssignmentStatement a => $"{Kw.Set} {ExpressionWriter.Write(a.Target, _language)} = {ExpressionWriter.Write(a.Value, _language)}",
        GotoStatement g => $"{Kw.Goto} {g.Label}",
        CallStatement c => $"{Kw.Call} {c.Name}",
        ReturnStatement => Kw.Return,
        _ => ReportInline(action),
    };

    private string ReportInline(Statement action)
    {
        _diagnostics.Report(DiagnosticCodes.UnloweredConstruct, action.Span, action.GetType().Name);
        return "<error>";
    }

    private void EmitComments(IReadOnlyList<string> comments)
    {
        foreach (var comment in comments)
        {
            _sb.Append(Indent()).Append(_language.Comment.Line).Append(' ').AppendLine(comment);
        }
    }

    private void Line(string text, string? trailingComment)
    {
        _sb.Append(Indent()).Append(text);
        AppendComment(_sb, trailingComment);
        _sb.AppendLine();
    }

    private StringBuilder AppendComment(StringBuilder sb, string? comment)
    {
        if (!string.IsNullOrEmpty(comment))
        {
            sb.Append(' ').Append(_language.Comment.Line).Append(' ').Append(comment);
        }

        return sb;
    }

    private string Indent() => new(' ', _indent * _format.IndentSize);
}
