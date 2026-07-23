namespace Transpiler.Engine.Syntax;

/// <summary>
/// Recursive-descent parser for the whole language family (SPEC §5, §6.1). The
/// statement set is selected by the language's capabilities (blockIf gates the
/// structured statements); keyword spellings come from the language's keyword table —
/// the grammar shape is fixed while every lexeme is configuration, which is what lets
/// users drop-ship new languages as JSON. Error recovery is panic-mode to end of
/// line: a malformed statement becomes a <see cref="SkippedStatement"/> with one
/// diagnostic and parsing continues.
///
/// One class, four files mirroring the grammar: this file (state, token access,
/// matching/recovery infrastructure), Parser.Sections.cs (ParseProgram as an
/// interpreter over the language's <see cref="SectionPlan"/> — the program skeleton
/// as data), Parser.Statements.cs (statement dispatch and block statements),
/// Parser.Expressions.cs (precedence-climbing expressions). Vendor marker lines are
/// recognized by <see cref="VendorLineRecognizer"/>, keeping tier-2 rule knowledge
/// out of the grammar core.
/// </summary>
public sealed partial class Parser
{
    private sealed class ParseErrorException : Exception
    {
    }

    private readonly SourceText _text;
    private readonly IReadOnlyList<Token> _tokens;
    private readonly LanguageProfile _language;
    private readonly VendorLineRecognizer _vendorLines;
    private readonly DiagnosticBag _diagnostics;
    private readonly List<string> _pendingComments = new();

    // The language's program skeleton and the plan rules the boundary predicates
    // consult on every stop check — resolved once, not per token. _compiled holds the
    // recipe patterns as ready-to-run regexes (cached per plan across parses).
    // _variables is the resolved variable model declarations dispatch on.
    private readonly SectionPlan _plan;
    private readonly VariablePlan _variables;
    private readonly CompiledSectionPlan _compiled;
    private readonly SectionRule[] _routineRules;
    private readonly SectionRule[] _fileRoutineRules;
    private readonly SectionRule? _mainRoutineRule;
    private readonly SectionRule? _subRoutineRule;

    // Loop variables discovered while desugaring FOR loops. They are implicitly local,
    // so ParseProgram materializes a LOCAL declaration for each (mirrors how a MATLAB
    // 'for i = ...' auto-declares i). Order-preserving + de-duplicating.
    private readonly List<string> _forLoopVariables = new();
    private int _position;

    public Parser(
        SourceText text,
        IReadOnlyList<Token> tokens,
        LanguageProfile language,
        IReadOnlyList<VendorPattern> vendorPatterns,
        DiagnosticBag diagnostics)
    {
        _text = text;
        _tokens = tokens;
        _language = language;
        _vendorLines = new VendorLineRecognizer(vendorPatterns);
        _diagnostics = diagnostics;

        _plan = language.Plan;
        _variables = language.Variables;
        _compiled = CompiledSectionPlan.For(language);
        _routineRules = _plan.RoutineRules.ToArray();
        _fileRoutineRules = _plan.FileSections.Where(static rule => rule.IsRoutine).ToArray();
        _mainRoutineRule = _plan.FindRule(SectionContent.MainRoutine);
        _subRoutineRule = _plan.FindRule(SectionContent.SubRoutine);
    }

    private KeywordTable Kw => _language.Keywords;

    /// <summary>Name used for routines the parser wraps implicitly (languages lacking a section level).</summary>
    private const string ImplicitSectionName = "Main";

    private Token Current => _tokens[Math.Min(_position, _tokens.Count - 1)];

    private Token Peek(int offset) => _tokens[Math.Min(_position + offset, _tokens.Count - 1)];

    private Token Advance()
    {
        var token = Current;
        if (_position < _tokens.Count - 1)
        {
            _position++;
        }

        return token;
    }

    /// <summary>End-of-line where recovery should stay inside the section.</summary>
    private void ExpectEndOfLineTolerantly()
    {
        try
        {
            ExpectEndOfLine();
        }
        catch (ParseErrorException)
        {
            SkipToEndOfLine();
        }
    }
    // ---------------------------------------------------------------- helpers

    private bool AtKeyword(string word) =>
        Current.Kind == TokenKind.Identifier &&
        string.Equals(Current.Text, word, _language.NameComparison);

    // ------------------------------------------------- block delimiter helpers

    private bool BraceBlocks => _language.Blocks.Style == BlockDelimiterStyle.Braces;

    /// <summary>Delimiters may be symbols ({, }) or words; match by text either way.</summary>
    private bool AtDelimiter(string delimiter) =>
        Current.Kind is TokenKind.OpenBrace or TokenKind.CloseBrace or TokenKind.Identifier &&
        string.Equals(Current.Text, delimiter, _language.NameComparison);

    private bool AtBlockClose() => AtDelimiter(_language.Blocks.Close);

    /// <summary>Consumes the opening delimiter and its end of line.</summary>
    private void ExpectBlockOpen()
    {
        if (!AtDelimiter(_language.Blocks.Open))
        {
            _diagnostics.Report(DiagnosticCodes.UnexpectedToken, Current.Span, Describe(Current), $"'{_language.Blocks.Open}'");
            throw new ParseErrorException();
        }

        Advance();
        ExpectEndOfLine();
    }

    /// <summary>Consumes the closing delimiter; the caller decides what may follow it.</summary>
    private void ExpectBlockClose()
    {
        if (!AtBlockClose())
        {
            _diagnostics.Report(DiagnosticCodes.UnexpectedToken, Current.Span, Describe(Current), $"'{_language.Blocks.Close}'");
            throw new ParseErrorException();
        }

        Advance();
    }

    private bool AtPhrase(string phrase)
    {
        // A null/blank keyword must never match: an empty phrase would otherwise
        // "match" unconditionally while consuming nothing (defense against language
        // files that null out a spelling the loader did not catch).
        if (string.IsNullOrWhiteSpace(phrase))
        {
            return false;
        }

        var words = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < words.Length; i++)
        {
            var token = Peek(i);
            if (token.Kind != TokenKind.Identifier ||
                !string.Equals(token.Text, words[i], _language.NameComparison))
            {
                return false;
            }
        }

        return true;
    }

    private bool TryMatchPhrase(string phrase, out int start)
    {
        start = Current.Span.Start;
        if (!AtPhrase(phrase))
        {
            return false;
        }

        var words = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < words.Length; i++)
        {
            Advance();
        }

        return true;
    }

    private void ExpectKeyword(string word)
    {
        if (!AtKeyword(word))
        {
            _diagnostics.Report(DiagnosticCodes.UnexpectedToken, Current.Span, Describe(Current), $"'{word}'");
            throw new ParseErrorException();
        }

        Advance();
    }

    /// <summary>Consumes a keyword when it is present; returns whether it was. Never reports.</summary>
    private bool AcceptKeyword(string word)
    {
        if (!AtKeyword(word))
        {
            return false;
        }

        Advance();
        return true;
    }

    /// <summary>
    /// Consumes a connector keyword between clauses (IF…THEN, WHILE…DO). When the
    /// language requires it (<paramref name="required"/>) a missing one is an error;
    /// otherwise the keyword is optional and simply skipped when absent — this is what
    /// lets a THEN/DO-free language (MATLAB) share the block grammar.
    /// </summary>
    private void ExpectConnector(string word, bool required)
    {
        if (required)
        {
            ExpectKeyword(word);
        }
        else
        {
            AcceptKeyword(word);
        }
    }

    private Token Expect(TokenKind kind)
    {
        if (Current.Kind != kind)
        {
            _diagnostics.Report(DiagnosticCodes.UnexpectedToken, Current.Span, Describe(Current), kind.ToString());
            throw new ParseErrorException();
        }

        return Advance();
    }

    private string ExpectIdentifier() => Expect(TokenKind.Identifier).Text;

    private string? ExpectEndOfLine()
    {
        // Trailing statement terminators come from the language's breakpoint
        // configuration: ";," for MATLAB, "" (or the field absent) for strictly
        // newline-terminated languages. Only listed characters are tolerated.
        var breakpoint = _language.Breakpoint ?? string.Empty;
        while ((Current.Kind == TokenKind.Semicolon && breakpoint.Contains(';')) ||
               (Current.Kind == TokenKind.Comma && breakpoint.Contains(',')))
        {
            Advance();
        }

        if (Current.Kind == TokenKind.EndOfLine)
        {
            return Advance().Comment is { Length: > 0 } comment ? comment : null;
        }

        if (Current.Kind == TokenKind.EndOfFile)
        {
            return Current.Comment is { Length: > 0 } comment ? comment : null;
        }

        _diagnostics.Report(DiagnosticCodes.UnexpectedToken, Current.Span, Describe(Current), "end of line");
        throw new ParseErrorException();
    }

    private void SkipBlankLines()
    {
        // Iterative on purpose: a long run of Bad tokens (a binary file opened by
        // mistake lexes to one Bad token per byte) must not grow the call stack —
        // StackOverflowException is uncatchable and would kill the process.
        while (true)
        {
            if (Current.Kind == TokenKind.EndOfLine)
            {
                if (Current.Comment is { Length: > 0 } comment)
                {
                    _pendingComments.Add(comment);
                }

                Advance();
            }
            else if (Current.Kind == TokenKind.Bad)
            {
                Advance();
            }
            else
            {
                return;
            }
        }
    }

    private void SkipToEndOfLine()
    {
        while (Current.Kind is not TokenKind.EndOfLine and not TokenKind.EndOfFile)
        {
            Advance();
        }

        if (Current.Kind == TokenKind.EndOfLine)
        {
            Advance();
        }
    }

    private string? SkipToEndOfLineCapturingComment(bool consumeEol)
    {
        while (Current.Kind is not TokenKind.EndOfLine and not TokenKind.EndOfFile)
        {
            Advance();
        }

        if (!consumeEol)
        {
            return null;
        }

        return ExpectEndOfLine();
    }

    private string RawLine()
    {
        // Slice from the current token to the END of the last real token on the line,
        // so trailing comment text (lexed as trivia, not tokens) is excluded — vendor
        // lift patterns are $-anchored and must still match commented marker lines.
        return RawLineFrom(Current.Span.Start);
    }

    private string RawLineFrom(int startOffset)
    {
        var end = Math.Max(startOffset, Current.Span.Start);
        var offset = 0;
        while (Peek(offset).Kind is not TokenKind.EndOfLine and not TokenKind.EndOfFile)
        {
            end = Peek(offset).Span.End;
            offset++;
        }

        return _text.ToString(TextSpan.FromBounds(startOffset, Math.Max(startOffset, end))).TrimEnd();
    }

    private IReadOnlyList<string> DrainComments()
    {
        if (_pendingComments.Count == 0)
        {
            return Array.Empty<string>();
        }

        var comments = _pendingComments.ToArray();
        _pendingComments.Clear();
        return comments;
    }

    private static string Describe(Token token) => token.Kind switch
    {
        TokenKind.EndOfLine => "end of line",
        TokenKind.EndOfFile => "end of file",
        _ => $"'{token.Text}'",
    };

    // Statements are constructed inside the parse methods without comment info;
    // rebuild the outermost node with its real extent + comments attached.
    private static Statement Reattach(Statement statement, TextSpan span, IReadOnlyList<string> comments, string? trailing) =>
        CloneWith(statement, span, comments, trailing);

    private static Statement CloneWith(Statement s, TextSpan span, IReadOnlyList<string> comments, string? trailing)
    {
        return s switch
        {
            AssignmentStatement a => new AssignmentStatement(a.Target, a.Value) { Span = span, LeadingComments = comments, TrailingComment = trailing },
            SetStatement st => new SetStatement(st.Value, st.Target) { Span = span, LeadingComments = comments, TrailingComment = trailing },
            IfBlockStatement i => new IfBlockStatement(i.Branches, i.ElseBody) { Span = span, LeadingComments = comments, TrailingComment = trailing },
            IfActionStatement ia => new IfActionStatement(ia.Condition, ia.ThenAction, ia.ElseAction) { Span = span, LeadingComments = comments, TrailingComment = trailing },
            WhileStatement w => new WhileStatement(w.Condition, w.Body) { Span = span, LeadingComments = comments, TrailingComment = trailing },
            RepeatStatement r => new RepeatStatement(r.Count, r.Body) { Span = span, LeadingComments = comments, TrailingComment = trailing },
            TryStatement t => new TryStatement(t.FaultVariable, t.Body, t.Handler) { Span = span, LeadingComments = comments, TrailingComment = trailing },
            ArrayDeclarationStatement ad => new ArrayDeclarationStatement(ad.Name, ad.Size) { Span = span, LeadingComments = comments, TrailingComment = trailing },
            GotoStatement g => new GotoStatement(g.Label) { Span = span, LeadingComments = comments, TrailingComment = trailing },
            LabelStatement l => new LabelStatement(l.Name) { Span = span, LeadingComments = comments, TrailingComment = trailing },
            CallStatement c => new CallStatement(c.Name) { Span = span, LeadingComments = comments, TrailingComment = trailing },
            ReturnStatement => new ReturnStatement { Span = span, LeadingComments = comments, TrailingComment = trailing },
            MarkerStatement m => new MarkerStatement(m.RuleName, m.MappingId, m.Role, m.Captures, m.Text) { Span = span, LeadingComments = comments, TrailingComment = trailing },
            SkippedStatement sk => new SkippedStatement(sk.RawText) { Span = span, LeadingComments = comments, TrailingComment = trailing },
            _ => s,
        };
    }
}
