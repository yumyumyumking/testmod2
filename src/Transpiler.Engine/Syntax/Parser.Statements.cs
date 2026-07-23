namespace Transpiler.Engine.Syntax;

public sealed partial class Parser
{
    // ------------------------------------------------------------- statements

    private List<Statement> ParseStatements(Func<bool> stop)
    {
        var statements = new List<Statement>();
        while (true)
        {
            SkipBlankLines();
            if (Current.Kind == TokenKind.EndOfFile || stop())
            {
                return statements;
            }

            // A counting FOR desugars to more than one statement (init + WHILE), so it
            // is expanded here rather than through the single-statement ParseStatement.
            if (_language.Capabilities.ForLoops && AtKeyword(Kw.For))
            {
                statements.AddRange(ParseForDesugared());
                continue;
            }

            statements.Add(ParseStatement());
        }
    }

    private Statement ParseStatement()
    {
        var comments = DrainComments();
        var start = Current.Span.Start;
        try
        {
            var statement = ParseStatementCore(consumeEol: true, out var trailing);
            return Reattach(statement, TextSpan.FromBounds(start, Current.Span.Start), comments, trailing);
        }
        catch (ParseErrorException)
        {
            // Recover the WHOLE line from the statement's start — tokens consumed
            // before the failure point must not vanish from the skipped text.
            var raw = RawLineFrom(start);
            SkipToEndOfLine();
            return new SkippedStatement(raw)
            {
                Span = TextSpan.FromBounds(start, Current.Span.Start),
                LeadingComments = comments,
            };
        }
    }

    /// <summary>
    /// Parses one statement. When <paramref name="consumeEol"/> is false the statement
    /// is an inline CL action (after THEN/ELSE) and must not swallow the line ending.
    /// </summary>
    private Statement ParseStatementCore(bool consumeEol, out string? trailing)
    {
        trailing = null;

        if (AtKeyword(Kw.If))
        {
            return ParseIf(consumeEol, ref trailing);
        }

        if (AtKeyword(Kw.Set) || AtKeyword(Kw.Reset))
        {
            var isSet = AtKeyword(Kw.Set);
            Advance();
            var target = ParseTarget();
            Statement result;
            if (isSet && Current.Kind == TokenKind.Equals)
            {
                Advance();
                var value = ParseExpression();
                result = new AssignmentStatement(target, value);
            }
            else
            {
                result = new SetStatement(isSet, target);
            }

            trailing = consumeEol ? ExpectEndOfLine() : null;
            return result;
        }

        if (AtKeyword(Kw.Goto))
        {
            Advance();
            var label = ExpectIdentifier();
            trailing = consumeEol ? ExpectEndOfLine() : null;
            return new GotoStatement(label);
        }

        if (AtKeyword(Kw.Call))
        {
            Advance();
            var name = ExpectIdentifier();
            trailing = consumeEol ? ExpectEndOfLine() : null;
            return new CallStatement(name);
        }

        if (AtKeyword(Kw.Return))
        {
            Advance();
            trailing = consumeEol ? ExpectEndOfLine() : null;
            return new ReturnStatement();
        }

        if (_language.Capabilities.BlockIf)
        {
            if (AtKeyword(Kw.While))
            {
                return ParseWhile(ref trailing);
            }

            if (AtKeyword(Kw.Repeat))
            {
                return ParseRepeat(ref trailing);
            }

            if (AtKeyword(Kw.Try))
            {
                return ParseTry(ref trailing);
            }

            if (AtKeyword(Kw.Array))
            {
                Advance();
                var name = ExpectIdentifier();
                Expect(TokenKind.OpenBracket);
                var size = Expect(TokenKind.Number).Text;
                Expect(TokenKind.CloseBracket);
                trailing = ExpectEndOfLine();
                return new ArrayDeclarationStatement(name, size);
            }
        }

        if (Current.Kind == TokenKind.Identifier)
        {
            // Label definition: NAME :
            if (Peek(1).Kind == TokenKind.Colon)
            {
                var name = Advance().Text;
                Advance(); // ':'
                trailing = consumeEol ? ExpectEndOfLine() : null;
                return new LabelStatement(name);
            }

            // Bare assignment: NAME [index] = expr  (canonical CLX; tolerated in CL)
            if (Peek(1).Kind is TokenKind.Equals or TokenKind.OpenBracket)
            {
                var target = ParseTarget();
                Expect(TokenKind.Equals);
                var value = ParseExpression();
                trailing = consumeEol ? ExpectEndOfLine() : null;
                return new AssignmentStatement(target, value);
            }

            // Implicit call: NAME ( args )  →  CALL NAME  (juxtaposition-call languages;
            // arguments are discarded, since CL routines take none). Brackets, not parens,
            // index in this grammar, so a parenthesized identifier is unambiguously a call.
            if (_language.Capabilities.ImplicitCall && Peek(1).Kind == TokenKind.OpenParen)
            {
                var callName = Advance().Text;
                SkipOptionalParameterList();
                trailing = consumeEol ? ExpectEndOfLine() : null;
                return new CallStatement(callName);
            }

            // Vendor marker line contributed by a mapping rule (tier-2 concern,
            // recognized outside the grammar core).
            var marker = _vendorLines.TryMatch(RawLine(), Current.Span, _diagnostics);
            if (marker is not null)
            {
                trailing = SkipToEndOfLineCapturingComment(consumeEol);
                return marker;
            }
        }

        _diagnostics.Report(DiagnosticCodes.UnknownStatement, Current.Span, RawLine());
        throw new ParseErrorException();
    }

    private Statement ParseIf(bool consumeEol, ref string? trailing)
    {
        Advance(); // IF
        var condition = ParseExpression();
        ExpectConnector(Kw.Then, _language.Capabilities.RequiresThen);

        if (!_language.Capabilities.BlockIf)
        {
            var thenAction = ParseStatementCore(consumeEol: false, out _);
            Statement? elseAction = null;
            if (AtKeyword(Kw.Else))
            {
                Advance();
                elseAction = ParseStatementCore(consumeEol: false, out _);
            }

            trailing = consumeEol ? ExpectEndOfLine() : null;
            return new IfActionStatement(condition, thenAction, elseAction);
        }

        // CLX brace block form: IF cond THEN { body } [ELSIF cond THEN { … }] [ELSE { … }]
        if (BraceBlocks && AtDelimiter(_language.Blocks.Open))
        {
            ExpectBlockOpen();
            var braceBranches = new List<IfBranch>
            {
                new(condition, ParseStatements(AtBlockClose)),
            };
            ExpectBlockClose();

            while (TryMatchPhrase(Kw.ElseIf, out _))
            {
                var elsifCondition = ParseExpression();
                ExpectConnector(Kw.Then, _language.Capabilities.RequiresThen);
                ExpectBlockOpen();
                braceBranches.Add(new IfBranch(elsifCondition, ParseStatements(AtBlockClose)));
                ExpectBlockClose();
            }

            IReadOnlyList<Statement>? braceElse = null;
            if (TryMatchPhrase(Kw.Else, out _))
            {
                ExpectBlockOpen();
                braceElse = ParseStatements(AtBlockClose);
                ExpectBlockClose();
            }

            trailing = ExpectEndOfLine();
            return new IfBlockStatement(braceBranches, braceElse);
        }

        // CLX keyword block form: IF cond THEN <EOL> body [ELSIF...] [ELSE...] ENDIF
        if (!BraceBlocks && Current.Kind == TokenKind.EndOfLine)
        {
            trailing = ExpectEndOfLine(); // head-line comment belongs to the IF itself
            var branches = new List<IfBranch>
            {
                new(condition, ParseStatements(AtIfBodyBoundary)),
            };

            while (TryMatchPhrase(Kw.ElseIf, out _))
            {
                var elsifCondition = ParseExpression();
                ExpectConnector(Kw.Then, _language.Capabilities.RequiresThen);
                PushPendingComment(ExpectEndOfLine());
                branches.Add(new IfBranch(elsifCondition, ParseStatements(AtIfBodyBoundary)));
            }

            IReadOnlyList<Statement>? elseBody = null;
            if (TryMatchPhrase(Kw.Else, out _))
            {
                PushPendingComment(ExpectEndOfLine());
                elseBody = ParseStatements(() => AtPhrase(Kw.EndIf) || AtEnclosingSectionBoundary());
            }

            if (!TryMatchPhrase(Kw.EndIf, out _))
            {
                // Missing terminator: one diagnostic, keep what parsed, and leave the
                // enclosing-section token for the caller instead of cascading.
                _diagnostics.Report(DiagnosticCodes.SectionNotClosed, Current.Span, Kw.If, Kw.EndIf);
                return new IfBlockStatement(branches, elseBody);
            }

            PushPendingComment(ExpectEndOfLine());
            return new IfBlockStatement(branches, elseBody);
        }

        // CLX inline form (migration convenience): IF cond THEN action [ELSE action]
        var inlineThen = ParseStatementCore(consumeEol: false, out _);
        IReadOnlyList<Statement>? inlineElse = null;
        if (AtKeyword(Kw.Else))
        {
            Advance();
            inlineElse = new[] { ParseStatementCore(consumeEol: false, out _) };
        }

        trailing = consumeEol ? ExpectEndOfLine() : null;
        return new IfBlockStatement(new[] { new IfBranch(condition, new[] { inlineThen }) }, inlineElse);
    }

    private bool AtIfBodyBoundary() =>
        AtPhrase(Kw.ElseIf) || AtPhrase(Kw.Else) || AtPhrase(Kw.EndIf) || AtEnclosingSectionBoundary();

    private Statement ParseWhile(ref string? trailing)
    {
        Advance();
        var condition = ParseExpression();
        ExpectConnector(Kw.Do, _language.Capabilities.RequiresDo);

        if (BraceBlocks)
        {
            ExpectBlockOpen();
            var braceBody = ParseStatements(AtBlockClose);
            ExpectBlockClose();
            trailing = ExpectEndOfLine();
            return new WhileStatement(condition, braceBody);
        }

        trailing = ExpectEndOfLine(); // head-line comment belongs to the WHILE itself
        var body = ParseStatements(() => AtPhrase(Kw.EndWhile) || AtEnclosingSectionBoundary());
        if (!TryMatchPhrase(Kw.EndWhile, out _))
        {
            _diagnostics.Report(DiagnosticCodes.SectionNotClosed, Current.Span, Kw.While, Kw.EndWhile);
            return new WhileStatement(condition, body);
        }

        PushPendingComment(ExpectEndOfLine());
        return new WhileStatement(condition, body);
    }

    private Statement ParseRepeat(ref string? trailing)
    {
        Advance();
        var count = ParseExpression();
        ExpectKeyword(Kw.Times);

        if (BraceBlocks)
        {
            ExpectBlockOpen();
            var braceBody = ParseStatements(AtBlockClose);
            ExpectBlockClose();
            trailing = ExpectEndOfLine();
            return new RepeatStatement(count, braceBody);
        }

        trailing = ExpectEndOfLine(); // head-line comment belongs to the REPEAT itself
        var body = ParseStatements(() => AtPhrase(Kw.EndRepeat) || AtEnclosingSectionBoundary());
        if (!TryMatchPhrase(Kw.EndRepeat, out _))
        {
            _diagnostics.Report(DiagnosticCodes.SectionNotClosed, Current.Span, Kw.Repeat, Kw.EndRepeat);
            return new RepeatStatement(count, body);
        }

        PushPendingComment(ExpectEndOfLine());
        return new RepeatStatement(count, body);
    }

    private Statement ParseTry(ref string? trailing)
    {
        Advance();

        if (BraceBlocks)
        {
            ExpectBlockOpen();
            var braceBody = ParseStatements(AtBlockClose);
            ExpectBlockClose();
            if (!TryMatchPhrase(Kw.Catch, out _))
            {
                _diagnostics.Report(DiagnosticCodes.SectionNotClosed, Current.Span, Kw.Try, Kw.Catch);
                throw new ParseErrorException();
            }

            var braceFault = ParseCatchFault();
            ExpectBlockOpen();
            var braceHandler = ParseStatements(AtBlockClose);
            ExpectBlockClose();
            trailing = ExpectEndOfLine();
            return new TryStatement(braceFault, braceBody, braceHandler);
        }

        trailing = ExpectEndOfLine(); // head-line comment belongs to the TRY itself
        var body = ParseStatements(() => AtPhrase(Kw.Catch) || AtEnclosingSectionBoundary());
        if (!TryMatchPhrase(Kw.Catch, out _))
        {
            _diagnostics.Report(DiagnosticCodes.SectionNotClosed, Current.Span, Kw.Try, Kw.Catch);
            return new TryStatement("<error>", body, Array.Empty<Statement>());
        }

        var faultVariable = ParseCatchFault();
        PushPendingComment(ExpectEndOfLine());
        var handler = ParseStatements(() => AtPhrase(Kw.EndTry) || AtEnclosingSectionBoundary());
        if (!TryMatchPhrase(Kw.EndTry, out _))
        {
            _diagnostics.Report(DiagnosticCodes.SectionNotClosed, Current.Span, Kw.Catch, Kw.EndTry);
            return new TryStatement(faultVariable, body, handler);
        }

        PushPendingComment(ExpectEndOfLine());
        return new TryStatement(faultVariable, body, handler);
    }

    /// <summary>Synthesized fault variable when a bare CATCH names none (MATLAB idiom).</summary>
    private const string DefaultFaultVariable = "ME";

    /// <summary>
    /// Parses the fault variable after CATCH. Parenthesized (<c>CATCH (e)</c>) when the
    /// language uses that form; otherwise a bare, optional identifier on the CATCH line
    /// (<c>catch e</c> / <c>catch</c>), synthesizing <see cref="DefaultFaultVariable"/>
    /// when omitted so the handler always has a name to bind.
    /// </summary>
    private string ParseCatchFault()
    {
        if (_language.Capabilities.CatchWithParens)
        {
            Expect(TokenKind.OpenParen);
            var name = ExpectIdentifier();
            Expect(TokenKind.CloseParen);
            return name;
        }

        return Current.Kind == TokenKind.Identifier ? Advance().Text : DefaultFaultVariable;
    }

    // ------------------------------------------------------------- FOR (desugared)

    /// <summary>
    /// Parses a counting FOR and desugars it to the equivalent init + WHILE, so no new
    /// IR node or transform pass is needed. On a parse error the whole line is retained
    /// as a single <see cref="SkippedStatement"/>, matching <see cref="ParseStatement"/>.
    /// </summary>
    private IReadOnlyList<Statement> ParseForDesugared()
    {
        var comments = DrainComments();
        var start = Current.Span.Start;
        try
        {
            return BuildFor(comments, start);
        }
        catch (ParseErrorException)
        {
            var raw = RawLineFrom(start);
            SkipToEndOfLine();
            return new Statement[]
            {
                new SkippedStatement(raw)
                {
                    Span = TextSpan.FromBounds(start, Current.Span.Start),
                    LeadingComments = comments,
                },
            };
        }
    }

    private IReadOnlyList<Statement> BuildFor(IReadOnlyList<string> comments, int start)
    {
        Advance(); // FOR
        var variable = ExpectIdentifier();
        Expect(TokenKind.Equals);
        var from = ParseExpression();
        Expect(TokenKind.Colon);
        var second = ParseExpression();

        // "from : to" (unit stride) or "from : stride : to".
        Expression stride = new NumberLiteral("1");
        Expression to;
        if (Current.Kind == TokenKind.Colon)
        {
            Advance();
            stride = second;
            to = ParseExpression();
        }
        else
        {
            to = second;
        }

        var headComment = ExpectEndOfLine();
        var body = ParseStatements(() => AtPhrase(Kw.EndFor) || AtEnclosingSectionBoundary());
        if (TryMatchPhrase(Kw.EndFor, out _))
        {
            PushPendingComment(ExpectEndOfLine());
        }
        else
        {
            _diagnostics.Report(DiagnosticCodes.SectionNotClosed, Current.Span, Kw.For, Kw.EndFor);
        }

        if (!_forLoopVariables.Contains(variable, _language.NameComparer))
        {
            _forLoopVariables.Add(variable);
        }

        var span = TextSpan.FromBounds(start, Current.Span.Start);
        var counter = new NameReference(variable);

        // v = from  — carries the loop's leading comments and head-line trailing comment.
        var init = new AssignmentStatement(counter, from)
        {
            Span = span,
            LeadingComments = comments,
            TrailingComment = headComment,
        };

        // while v <= to  (v >= to when the stride is a negative literal, i.e. a countdown).
        var guardOp = IsNegativeLiteral(stride) ? BinaryOperator.GreaterOrEqual : BinaryOperator.LessOrEqual;
        var loopBody = new List<Statement>(body)
        {
            new AssignmentStatement(counter, new BinaryExpression(BinaryOperator.Add, counter, stride)),
        };
        var loop = new WhileStatement(new BinaryExpression(guardOp, counter, to), loopBody) { Span = span };

        return new Statement[] { init, loop };
    }

    /// <summary>A negative numeric literal (unary minus over a number) — a countdown stride.</summary>
    private static bool IsNegativeLiteral(Expression stride) =>
        stride is UnaryExpression { Operator: UnaryOperator.Negate, Operand: NumberLiteral };
}
