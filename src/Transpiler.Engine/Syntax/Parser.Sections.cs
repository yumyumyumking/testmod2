using System.Text.RegularExpressions;

namespace Transpiler.Engine.Syntax;

public sealed partial class Parser
{
    // ---------------------------------------------------------------- program
    //
    // ParseProgram is an interpreter over the language's SectionPlan — the program
    // skeleton as data (which sections exist, at which level, in what priority
    // order, how often). The walk itself is fixed and small:
    //
    //   file prologue   the namespace header line, when the language has one
    //   file body       repeatedly: namespace terminator?  else first matching plan rule
    //   finish          cardinality checks, implicit wrappers, namespace-end check
    //
    // Everything language-shaped comes from the plan — authored as a JSON recipe or
    // synthesized from the legacy header block. Each rule spells its header either as
    // a keyword (built-in flexible parse: identifier name, optional out = name(args)
    // form) or as a typed-capture pattern matched against the raw line; the emit side
    // mirrors this in the Emitter. A new program skeleton is a JSON change, not a
    // parser change.

    public ProgramSyntax ParseProgram()
    {
        SkipBlankLines();
        var parts = new ProgramParts();

        ParseFileHeader(parts);
        ParseFileSections(parts);
        return FinishProgram(parts);
    }

    /// <summary>Everything a program walk accumulates before FinishProgram assembles the node.</summary>
    private sealed class ProgramParts
    {
        public ProgramHeader? Header { get; set; }

        public bool FileEndSeen { get; set; }

        public List<VariableDeclaration> Declarations { get; } = new();

        public List<MainRoutine> MainRoutines { get; } = new();

        public List<SubRoutine> FileRoutines { get; } = new();

        /// <summary>Sub-routines at file level (language without a main-routine section).</summary>
        public List<SubRoutine> ImplicitRoutines { get; } = new();

        /// <summary>Statements at file level (language without any routine sections).</summary>
        public List<Statement> ImplicitBody { get; } = new();

        private readonly Dictionary<SectionContent, int> _appearances = new();

        public void CountAppearance(SectionContent content) =>
            _appearances[content] = AppearancesOf(content) + 1;

        public int AppearancesOf(SectionContent content) =>
            _appearances.TryGetValue(content, out var count) ? count : 0;
    }

    // ---------------------------------------------------------- rule dispatch

    /// <summary>The word the parser dispatches on for a rule's header (keyword, or a pattern's leading literal).</summary>
    private static string? StartWordOf(SectionRule rule) =>
        rule.UsesPatterns ? rule.StartFirstWord : rule.Delimiters.Start;

    /// <summary>The word the parser dispatches on for a rule's terminator.</summary>
    private static string? EndWordOf(SectionRule rule) =>
        rule.EndPattern is not null ? rule.EndFirstWord : rule.Delimiters.End;

    /// <summary>How a rule's terminator is described in diagnostics.</summary>
    private static string EndDisplayOf(SectionRule rule) =>
        rule.EndPattern is not null ? SectionPatterns.ExpectedForm(rule.EndPattern) : rule.Delimiters.End!;

    /// <summary>
    /// The first plan rule whose section starts at the cursor; null when none does.
    /// Statements — where a plan has it — matches any line, which is why plan
    /// building puts it last.
    /// </summary>
    private SectionRule? FirstMatchingRule(IReadOnlyList<SectionRule> rules)
    {
        foreach (var rule in rules)
        {
            var matches = rule.Content switch
            {
                SectionContent.Declarations => AtDeclarationStart(),
                SectionContent.Statements => true,
                _ => AtPhrase(StartWordOf(rule)!),
            };

            if (matches)
            {
                return rule;
            }
        }

        return null;
    }

    // ------------------------------------------------------------ file walk

    /// <summary>
    /// File prologue, when the language has a namespace section: the recipe's header
    /// pattern (however shaped), or the keyword shorthand's plain <c>START Name</c>.
    /// With a terminator configured it wraps the whole program.
    /// </summary>
    private void ParseFileHeader(ProgramParts parts)
    {
        if (_plan.Namespace is not { } ns)
        {
            return;
        }

        if (AtPhrase(StartWordOf(ns)!))
        {
            parts.Header = ns.UsesPatterns ? ParsePatternHeader(ns) : ParseKeywordHeader(ns.Delimiters);
        }
        else
        {
            _diagnostics.Report(DiagnosticCodes.MissingHeader, Current.Span, ExpectedHeaderForm(ns));
        }
    }

    private static string ExpectedHeaderForm(SectionRule ns) => ns.UsesPatterns
        ? SectionPatterns.ExpectedForm(ns.StartPattern!)
        : $"{ns.Delimiters.Start} <name>";

    /// <summary>
    /// The file-body interpreter: each iteration consumes the namespace terminator
    /// (when one is expected) or hands the cursor to the first plan rule whose section
    /// starts here. A known section at the wrong level is CLX0116; a line no rule
    /// recognizes is CLX0103 — one diagnostic, one skipped line, either way.
    /// </summary>
    private void ParseFileSections(ProgramParts parts)
    {
        var ns = _plan.Namespace;
        var expectFileEnd = ns is not null && parts.Header is not null && ns.HasTerminator;

        SkipBlankLines();
        while (Current.Kind != TokenKind.EndOfFile)
        {
            if (expectFileEnd && AtPhrase(EndWordOf(ns!)!))
            {
                ConsumeRuleTerminator(ns!, parts.Header!.Name);
                parts.FileEndSeen = true;
            }
            else if (FirstMatchingRule(_plan.FileSections) is { } rule)
            {
                ParseFileSection(rule, parts);
                parts.CountAppearance(rule.Content);
            }
            else if (MisplacedRuleAtCursor() is { } misplaced)
            {
                _diagnostics.Report(DiagnosticCodes.SectionNotAllowedHere, Current.Span, misplaced.DisplayName,
                    $"at file level — it belongs inside '{_mainRoutineRule!.DisplayName}'");
                SkipToEndOfLine();
            }
            else
            {
                _diagnostics.Report(DiagnosticCodes.UnknownStatement, Current.Span, RawLine());
                SkipToEndOfLine();
            }

            SkipBlankLines();
        }
    }

    /// <summary>A main-routine-hosted section whose header starts at the cursor (wrong level).</summary>
    private SectionRule? MisplacedRuleAtCursor()
    {
        if (_mainRoutineRule is null)
        {
            return null;
        }

        foreach (var rule in _plan.MainRoutineSections)
        {
            if (rule.IsRoutine && !_plan.FileSections.Contains(rule) && AtPhrase(StartWordOf(rule)!))
            {
                return rule;
            }
        }

        return null;
    }

    private void ParseFileSection(SectionRule rule, ProgramParts parts)
    {
        switch (rule.Content)
        {
            case SectionContent.Declarations:
                parts.Declarations.Add(ParseDeclarationAtCursor());
                break;

            case SectionContent.MainRoutine:
                parts.MainRoutines.Add(ParseMainRoutine(rule, parts));
                break;

            case SectionContent.Function:
            case SectionContent.Handler:
                parts.FileRoutines.Add(ParseRoutine(rule));
                break;

            case SectionContent.SubRoutine:
                // No main-routine section in this language: sub-routines sit at file
                // level and are wrapped in one implicit main routine by FinishProgram.
                parts.ImplicitRoutines.Add(ParseRoutine(rule));
                break;

            case SectionContent.Statements:
                ParseFileStatements(parts);
                break;
        }
    }

    /// <summary>
    /// File-level bare statements (a language with no routine sections at all); the
    /// file body wraps into one implicit routine by FinishProgram. Guard against
    /// zero progress: a stray enclosing terminator (e.g. an unexpected file END when
    /// the header never parsed) satisfies the stop predicate immediately and would
    /// otherwise loop forever.
    /// </summary>
    private void ParseFileStatements(ProgramParts parts)
    {
        var before = _position;
        parts.ImplicitBody.AddRange(
            ParseStatements(() => AtSectionStart() || AtSectionTerminator() || AtDeclarationStart()));
        if (_position == before)
        {
            _diagnostics.Report(DiagnosticCodes.UnknownStatement, Current.Span, RawLine());
            SkipToEndOfLine();
        }
    }

    /// <summary>The declaration whose scope keyword is at the cursor (first matching scope entry).</summary>
    private VariableDeclaration ParseDeclarationAtCursor()
    {
        foreach (var scope in _variables.Scopes)
        {
            if (AtPhrase(scope.Keyword))
            {
                return ParseDeclaration(scope);
            }
        }

        // Unreachable: callers gate on AtDeclarationStart, which checks the same list.
        return ParseDeclaration(_variables.Scopes[0]);
    }

    /// <summary>
    /// Assembles the program node and applies the plan's after-walk rules:
    /// cardinality diagnostics (a required section that never appeared), the
    /// implicit wrappers for levels this language does not spell out, the missing
    /// namespace terminator, and FOR-loop counter locals.
    /// </summary>
    private ProgramSyntax FinishProgram(ProgramParts parts)
    {
        foreach (var rule in _plan.FileSections)
        {
            if (rule.Cardinality != SectionCardinality.OneOrMany || parts.AppearancesOf(rule.Content) != 0)
            {
                continue;
            }

            if (rule.Content == SectionContent.MainRoutine)
            {
                _diagnostics.Report(DiagnosticCodes.MissingMainRoutine, Current.Span, StartWordOf(rule)!);
            }
            else
            {
                _diagnostics.Report(DiagnosticCodes.RequiredSectionMissing, Current.Span, rule.DisplayName,
                    _plan.Namespace is { } host ? $"the '{host.DisplayName}' section" : "the file");
            }
        }

        // Content that sat one level up (absent sections) wraps implicitly: bare
        // statements into one routine, file-level routines into one main routine.
        if (_mainRoutineRule is null && (parts.ImplicitRoutines.Count > 0 || parts.ImplicitBody.Count > 0))
        {
            if (parts.ImplicitBody.Count > 0)
            {
                parts.ImplicitRoutines.Add(new SubRoutine(ImplicitSectionName, parts.ImplicitBody));
            }

            parts.MainRoutines.Add(new MainRoutine(ImplicitSectionName, parts.ImplicitRoutines));
        }

        if (parts.Header is not null && _plan.Namespace is { HasTerminator: true } ns && !parts.FileEndSeen)
        {
            _diagnostics.Report(DiagnosticCodes.SectionNotClosed, Current.Span,
                StartWordOf(ns) ?? ns.DisplayName, EndDisplayOf(ns));
        }

        MaterializeForLoopLocals(parts.Declarations);

        return new ProgramSyntax(parts.Header, parts.Declarations, parts.MainRoutines, parts.FileRoutines);
    }

    /// <summary>
    /// Materializes implicit locals for FOR counters that no explicit declaration
    /// covers — a counting-FOR language (MATLAB) auto-declares its loop variable.
    /// Only such languages populate the list, so this is inert elsewhere.
    /// </summary>
    private void MaterializeForLoopLocals(List<VariableDeclaration> declarations)
    {
        if (_forLoopVariables.Count == 0)
        {
            return;
        }

        var declared = new HashSet<string>(declarations.Select(static d => d.Name), _language.NameComparer);
        foreach (var name in _forLoopVariables)
        {
            if (declared.Add(name))
            {
                declarations.Add(new VariableDeclaration(VariableScopeKind.Local, name, VariableKind.Numeric));
            }
        }
    }

    // ------------------------------------------------------- pattern matching

    /// <summary>
    /// Parses a pattern-mode section header: the whole raw line is matched against the
    /// rule's compiled format. On success the well-known {name} capture is the
    /// instance name and everything else lands in <paramref name="extras"/>; on
    /// mismatch one CLX0117 names the expected form and the line is skipped —
    /// the same one-diagnostic-per-line recovery as everywhere else.
    /// </summary>
    private string ParsePatternSectionHead(
        SectionRule rule,
        out IReadOnlyDictionary<string, string> extras,
        out string? trailing)
    {
        extras = SyntaxNode.NoCaptures;
        var regex = _compiled.Rule(rule).Start!;
        var line = RawLine();

        Match match;
        try
        {
            match = regex.Match(line);
        }
        catch (RegexMatchTimeoutException)
        {
            _diagnostics.Report(DiagnosticCodes.ConfigInvalid, Current.Span,
                $"format pattern of section '{rule.DisplayName}' timed out matching this line; line skipped.");
            trailing = SkipToEndOfLineCapturingComment(consumeEol: true);
            return "<error>";
        }

        if (!match.Success)
        {
            _diagnostics.Report(DiagnosticCodes.SectionHeaderMismatch, Current.Span,
                rule.DisplayName, SectionPatterns.ExpectedForm(rule.StartPattern!));
            trailing = SkipToEndOfLineCapturingComment(consumeEol: true);
            return "<error>";
        }

        var captures = SectionPatterns.CapturesOf(regex, match);
        var name = captures.TryGetValue(SectionPatterns.NameCapture, out var value) ? value : "<error>";
        captures.Remove(SectionPatterns.NameCapture);
        extras = captures;
        trailing = SkipToEndOfLineCapturingComment(consumeEol: true);
        return name;
    }

    /// <summary>
    /// Pattern-mode namespace header → <see cref="ProgramHeader"/>: the {name}
    /// capture is the program's name, everything else the pattern captured is an
    /// opaque header field.
    /// </summary>
    private ProgramHeader ParsePatternHeader(SectionRule ns)
    {
        var comments = DrainComments();
        var start = Current.Span.Start;
        var name = ParsePatternSectionHead(ns, out var captures, out var trailing);

        return new ProgramHeader(name)
        {
            Span = TextSpan.FromBounds(start, Current.Span.Start),
            LeadingComments = comments,
            TrailingComment = trailing,
            Fields = name == "<error>" ? SyntaxNode.NoCaptures : captures,
        };
    }

    /// <summary>
    /// Consumes a section terminator whose dispatch word is at the cursor. Keyword
    /// mode goes through the built-in optional-name check; pattern mode matches the
    /// whole line (a pattern without {name} tolerates an optional trailing name,
    /// mirroring keyword terminators) and checks a captured name against the
    /// section's (CLX0113).
    /// </summary>
    private void ConsumeRuleTerminator(SectionRule rule, string sectionName)
    {
        if (rule.EndPattern is null)
        {
            TryMatchPhrase(rule.Delimiters.End!, out _);
            MatchOptionalEndName(rule.Delimiters.End!, sectionName);
            return;
        }

        var regex = _compiled.Rule(rule).End!;
        var line = RawLine();

        Match match;
        try
        {
            match = regex.Match(line);
        }
        catch (RegexMatchTimeoutException)
        {
            _diagnostics.Report(DiagnosticCodes.ConfigInvalid, Current.Span,
                $"terminator pattern of section '{rule.DisplayName}' timed out matching this line; line skipped.");
            SkipToEndOfLine();
            return;
        }

        if (!match.Success)
        {
            _diagnostics.Report(DiagnosticCodes.SectionHeaderMismatch, Current.Span,
                rule.DisplayName, SectionPatterns.ExpectedForm(rule.EndPattern));
            SkipToEndOfLine();
            return;
        }

        var nameGroup = match.Groups[SectionPatterns.NameCapture];
        if (nameGroup.Success && sectionName != "<error>" &&
            !string.Equals(nameGroup.Value, sectionName, _language.NameComparison))
        {
            _diagnostics.Report(DiagnosticCodes.EndNameMismatch, Current.Span,
                rule.EndFirstWord!, nameGroup.Value, sectionName);
        }

        PushPendingComment(SkipToEndOfLineCapturingComment(consumeEol: true));
    }

    // ---------------------------------------------------------------- sections

    /// <summary>
    /// Keyword-mode namespace header: <c>START Name</c>, like every other keyword
    /// section head. A language whose header line carries more than the name spells
    /// it as a <c>format</c> pattern — the shape lives in the recipe, not here.
    /// </summary>
    private ProgramHeader ParseKeywordHeader(DelimiterPair file)
    {
        var comments = DrainComments();
        TryMatchPhrase(file.Start!, out var start);
        try
        {
            var name = ExpectIdentifier();
            var trailing = ExpectEndOfLine();

            return new ProgramHeader(name)
            {
                Span = TextSpan.FromBounds(start, Current.Span.Start),
                LeadingComments = comments,
                TrailingComment = trailing,
            };
        }
        catch (ParseErrorException)
        {
            SkipToEndOfLine();
            return new ProgramHeader("<error>")
            {
                Span = TextSpan.FromBounds(start, Current.Span.Start),
                LeadingComments = comments,
            };
        }
    }

    /// <summary>
    /// Parses one declaration under a scope entry of the language's variable model:
    /// the scope's keyword, a name, an optional kind annotation (any configured kind
    /// spelling), and — as the scope's binding policy allows — a point binding
    /// (<c>!Area.PT(index)</c>). Kind is inferred from the binding's point type when
    /// no annotation was given; a forbidden binding is CLX0121, a missing required
    /// one CLX0122.
    /// </summary>
    private VariableDeclaration ParseDeclaration(VariableScopeRule scope)
    {
        var comments = DrainComments();
        TryMatchPhrase(scope.Keyword, out var start);
        var name = "<error>";
        VariableKind? kind = null;
        PointBinding? binding = null;
        string? trailing = null;
        try
        {
            name = ExpectIdentifier();

            // Optional kind annotation:  : <spelling of a configured kind>
            if (Current.Kind == TokenKind.Colon)
            {
                Advance();
                var kindRule = _variables.Kinds.FirstOrDefault(rule => AtKeyword(rule.Spelling));
                if (kindRule is null)
                {
                    _diagnostics.Report(DiagnosticCodes.UnexpectedToken, Current.Span, Describe(Current),
                        _variables.SupportsKinds
                            ? string.Join(" or ", _variables.Kinds.Select(static rule => $"'{rule.Spelling}'"))
                            : "no kind annotation (this language declares none)");
                    throw new ParseErrorException();
                }

                Advance();
                kind = kindRule.Kind;
            }

            // Point binding:  !Area.NN(index) — as the scope's policy allows.
            if (Current.Kind == TokenKind.Bang)
            {
                Advance();
                var area = ExpectIdentifier();
                Expect(TokenKind.Dot);
                var pointType = ExpectIdentifier();
                Expect(TokenKind.OpenParen);
                var index = Expect(TokenKind.Number).Text;
                Expect(TokenKind.CloseParen);
                binding = new PointBinding(area, pointType, index);

                if (scope.Binding == BindingPolicy.Forbidden)
                {
                    _diagnostics.Report(DiagnosticCodes.BindingNotAllowed, Current.Span, scope.Keyword);
                }

                // Infer the kind from the point type when no annotation was given.
                kind ??= _variables.KindForPoint(pointType, _language.NameComparison)?.Kind ?? VariableKind.Numeric;
            }
            else if (scope.Binding == BindingPolicy.Required)
            {
                _diagnostics.Report(DiagnosticCodes.BindingRequired, Current.Span, scope.Keyword);
            }

            trailing = ExpectEndOfLine();
        }
        catch (ParseErrorException)
        {
            SkipToEndOfLine();
        }

        return new VariableDeclaration(scope.Kind, name, kind, binding)
        {
            Span = TextSpan.FromBounds(start, Current.Span.Start),
            LeadingComments = comments,
            TrailingComment = trailing,
        };
    }

    /// <summary>
    /// Parses one main-routine section (PHASE): the header line (keyword or pattern),
    /// then an interpreter loop over the plan's main-routine alternatives —
    /// sub-routines, hoisted declarations, or (in a language without a sub-routine
    /// section) bare statements that wrap into one implicit subRoutine. Sections the plan
    /// marks OneOrMany at this level are checked per instance.
    /// </summary>
    private MainRoutine ParseMainRoutine(SectionRule rule, ProgramParts parts)
    {
        var comments = DrainComments();
        var start = Current.Span.Start;
        var name = "<error>";
        string? headComment = null;
        var extras = SyntaxNode.NoCaptures;

        if (rule.UsesPatterns)
        {
            name = ParsePatternSectionHead(rule, out extras, out headComment);
        }
        else
        {
            TryMatchPhrase(rule.Delimiters.Start!, out start);
            try
            {
                name = ExpectIdentifier();
                headComment = ExpectEndOfLine();
            }
            catch (ParseErrorException)
            {
                SkipToEndOfLine();
            }
        }

        var subRoutines = new List<SubRoutine>();
        List<Statement>? inlineBody = null;
        var appearances = new Dictionary<SectionContent, int>();

        // A terminator-less section — or one whose terminator is optional (CL's PHASE) —
        // just ends at the next boundary; a required terminator that is never seen is
        // reported as CLX0110 below.
        var closed = !rule.HasTerminator || rule.EndOptional;
        while (true)
        {
            SkipBlankLines();
            if (rule.HasTerminator && AtPhrase(EndWordOf(rule)!))
            {
                ConsumeRuleTerminator(rule, name);
                closed = true;
                break;
            }

            if (Current.Kind == TokenKind.EndOfFile || AtMainRoutineBoundary())
            {
                break;
            }

            if (FirstMatchingRule(_plan.MainRoutineSections) is { } item)
            {
                ParseMainRoutineItem(item, parts, subRoutines, ref inlineBody);
                appearances[item.Content] = appearances.TryGetValue(item.Content, out var count) ? count + 1 : 1;
            }
            else
            {
                // Only reachable when the plan has a sub-routine section (plan
                // building guarantees a main routine hosts sub-routines or bare
                // statements): a statement inside the main routine has no home.
                _diagnostics.Report(DiagnosticCodes.StatementOutsideSubRoutine, Current.Span,
                    _subRoutineRule is { } subRoutine ? StartWordOf(subRoutine)! : "subroutine");
                SkipToEndOfLine();
            }
        }

        if (inlineBody is { Count: > 0 })
        {
            subRoutines.Add(new SubRoutine(name, inlineBody));
        }

        if (!closed)
        {
            _diagnostics.Report(DiagnosticCodes.SectionNotClosed, Current.Span,
                StartWordOf(rule)!, EndDisplayOf(rule));
        }

        foreach (var item in _plan.MainRoutineSections)
        {
            if (item.Cardinality == SectionCardinality.OneOrMany && !appearances.ContainsKey(item.Content))
            {
                _diagnostics.Report(DiagnosticCodes.RequiredSectionMissing, Current.Span,
                    item.DisplayName, $"'{name}' ({rule.DisplayName})");
            }
        }

        return new MainRoutine(name, subRoutines)
        {
            Span = TextSpan.FromBounds(start, Current.Span.Start),
            LeadingComments = comments,
            TrailingComment = headComment,
            ExtraCaptures = extras,
        };
    }

    private void ParseMainRoutineItem(SectionRule rule, ProgramParts parts, List<SubRoutine> subRoutines, ref List<Statement>? inlineBody)
    {
        switch (rule.Content)
        {
            case SectionContent.Declarations:
                // Declarations hoist into file scope from any section level.
                parts.Declarations.Add(ParseDeclarationAtCursor());
                break;

            case SectionContent.SubRoutine:
                subRoutines.Add(ParseRoutine(rule));
                break;

            case SectionContent.Statements:
                // No sub-routine section in this language: statements sit directly in
                // the main routine and wrap into one implicit sub-routine by ParseMainRoutine.
                (inlineBody ??= new List<Statement>()).AddRange(
                    ParseStatements(() => AtMainRoutineBoundary() || AtDeclarationStart()));
                break;
        }
    }

    /// <summary>
    /// Parses one routine section: a sub-routine (STEP) inside a main routine, or a
    /// file-level Function/Handler routine. The rule supplies the header spelling
    /// (keyword or pattern), the terminator, and the <see cref="SectionSlots"/> tag
    /// stored on the node.
    /// </summary>
    private SubRoutine ParseRoutine(SectionRule rule)
    {
        if (rule.UsesPatterns)
        {
            return ParsePatternRoutine(rule);
        }

        var pair = rule.Delimiters;
        var comments = DrainComments();
        TryMatchPhrase(pair.Start!, out var start);
        var name = "<error>";
        string? headComment = null;
        try
        {
            name = ExpectIdentifier();

            // Return-value header form "function out = name(...)": the first identifier
            // was the output variable; the routine name follows '='. (CL/CLX routine
            // headers never carry '=' or a parameter list, so this is a no-op for them.)
            if (Current.Kind == TokenKind.Equals)
            {
                Advance();
                name = ExpectIdentifier();
            }

            SkipOptionalParameterList();

            if (BraceBlocks)
            {
                ExpectBlockOpen();
            }
            else
            {
                headComment = ExpectEndOfLine();
            }
        }
        catch (ParseErrorException)
        {
            SkipToEndOfLine();
        }

        List<Statement> body;
        if (BraceBlocks)
        {
            body = ParseStatements(() => AtBlockClose() || AtSectionStart() || AtSectionTerminator());
            if (AtBlockClose())
            {
                Advance();
                ExpectEndOfLineTolerantly();
            }
            else
            {
                _diagnostics.Report(DiagnosticCodes.SectionNotClosed, Current.Span, pair.Start!, _language.Blocks.Close);
            }
        }
        else if (pair.HasTerminator)
        {
            // Body runs until the end keyword; the next section start also terminates (recovery).
            body = ParseStatements(() => AtPhrase(pair.End!) || AtSectionStart() || AtSectionTerminator());

            if (TryMatchPhrase(pair.End!, out _))
            {
                MatchOptionalEndName(pair.End!, name);
            }
            else if (!rule.EndOptional)
            {
                _diagnostics.Report(DiagnosticCodes.SectionNotClosed, Current.Span, pair.Start!, $"{pair.End} {name}");
            }
        }
        else
        {
            // No terminator in this language: the routine ends at the next section or EOF.
            body = ParseStatements(() => AtSectionStart() || AtSectionTerminator());
        }

        return new SubRoutine(name, body, rule.RoutineKind)
        {
            Span = TextSpan.FromBounds(start, Current.Span.Start),
            LeadingComments = comments,
            TrailingComment = headComment,
        };
    }

    /// <summary>Pattern-mode routine: pattern header line, statement body, pattern terminator.</summary>
    private SubRoutine ParsePatternRoutine(SectionRule rule)
    {
        var comments = DrainComments();
        var start = Current.Span.Start;
        var name = ParsePatternSectionHead(rule, out var extras, out var headComment);

        List<Statement> body;
        if (rule.HasTerminator)
        {
            body = ParseStatements(() => AtPhrase(EndWordOf(rule)!) || AtSectionStart() || AtSectionTerminator());
            if (AtPhrase(EndWordOf(rule)!))
            {
                ConsumeRuleTerminator(rule, name);
            }
            else if (!rule.EndOptional)
            {
                _diagnostics.Report(DiagnosticCodes.SectionNotClosed, Current.Span,
                    StartWordOf(rule)!, EndDisplayOf(rule));
            }
        }
        else
        {
            body = ParseStatements(() => AtSectionStart() || AtSectionTerminator());
        }

        return new SubRoutine(name, body, rule.RoutineKind)
        {
            Span = TextSpan.FromBounds(start, Current.Span.Start),
            LeadingComments = comments,
            TrailingComment = headComment,
            ExtraCaptures = extras,
        };
    }

    /// <summary>
    /// After a section terminator: an optional repeated section name, checked when
    /// present (CL writes "END StepName"; other languages may write a bare "ENDSTEP").
    /// No mismatch is reported against the "&lt;error&gt;" sentinel of a section whose
    /// own header failed to parse — that would be a second diagnostic for one mistake.
    /// The terminator line's comment attaches to whatever follows.
    /// </summary>
    private void MatchOptionalEndName(string endKeyword, string sectionName)
    {
        try
        {
            if (Current.Kind == TokenKind.Identifier)
            {
                var nameToken = Advance();
                if (sectionName != "<error>" &&
                    !string.Equals(nameToken.Text, sectionName, _language.NameComparison))
                {
                    _diagnostics.Report(DiagnosticCodes.EndNameMismatch, nameToken.Span, endKeyword, nameToken.Text, sectionName);
                }
            }

            PushPendingComment(ExpectEndOfLine());
        }
        catch (ParseErrorException)
        {
            SkipToEndOfLine();
        }
    }

    /// <summary>
    /// Skips a routine header's parameter list — "(a, b)" or "()" — when present. CL has
    /// no argument passing, so parameters are intentionally discarded (a routine maps to
    /// an argument-less CALL). Balanced on the header line only.
    /// </summary>
    private void SkipOptionalParameterList()
    {
        if (Current.Kind != TokenKind.OpenParen)
        {
            return;
        }

        var depth = 0;
        while (Current.Kind is not TokenKind.EndOfLine and not TokenKind.EndOfFile)
        {
            if (Current.Kind == TokenKind.OpenParen)
            {
                depth++;
            }
            else if (Current.Kind == TokenKind.CloseParen)
            {
                depth--;
            }

            Advance();
            if (depth == 0)
            {
                break;
            }
        }
    }

    /// <summary>Keeps a structural line's trailing comment for the next construct.</summary>
    private void PushPendingComment(string? comment)
    {
        if (!string.IsNullOrEmpty(comment))
        {
            _pendingComments.Add(comment);
        }
    }

    // ------------------------------------------------------ boundary predicates
    // All read the section plan, so the stop conditions of statement and routine
    // parsing agree with the interpreter about what counts as a section boundary.
    // Pattern-mode rules dispatch on their leading literal keyword here; the full
    // pattern is only run when the line is actually consumed.

    /// <summary>A configured section-start keyword (any routine-bearing plan rule) is at the cursor.</summary>
    private bool AtSectionStart()
    {
        foreach (var rule in _routineRules)
        {
            if (AtPhrase(StartWordOf(rule)!))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>An enclosing section's terminator is at the cursor (main-routine END, namespace END).</summary>
    private bool AtSectionTerminator()
    {
        if (_mainRoutineRule is { HasTerminator: true } mainRoutine && AtPhrase(EndWordOf(mainRoutine)!))
        {
            return true;
        }

        return _plan.Namespace is { HasTerminator: true } ns && AtPhrase(EndWordOf(ns)!);
    }

    /// <summary>A configured declaration-scope keyword is at the cursor.</summary>
    private bool AtDeclarationStart()
    {
        foreach (var scope in _variables.Scopes)
        {
            if (AtPhrase(scope.Keyword))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Where a main-routine section stops collecting content.</summary>
    private bool AtMainRoutineBoundary()
    {
        foreach (var rule in _fileRoutineRules)
        {
            if (AtPhrase(StartWordOf(rule)!))
            {
                return true;
            }
        }

        if (_mainRoutineRule is { HasTerminator: true } mainRoutine && AtPhrase(EndWordOf(mainRoutine)!))
        {
            return true;
        }

        return _plan.Namespace is { HasTerminator: true } ns && AtPhrase(EndWordOf(ns)!);
    }

    /// <summary>
    /// Any enclosing section boundary — a block statement (IF/WHILE/REPEAT/TRY)
    /// missing its terminator must stop here instead of consuming the rest of the
    /// file, so one mistake yields one diagnostic and the section still closes.
    /// </summary>
    private bool AtEnclosingSectionBoundary()
    {
        if (AtSectionStart() || AtSectionTerminator())
        {
            return true;
        }

        return _subRoutineRule is { HasTerminator: true } subRoutine && AtPhrase(EndWordOf(subRoutine)!);
    }
}
