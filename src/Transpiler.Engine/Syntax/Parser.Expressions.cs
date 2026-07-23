namespace Transpiler.Engine.Syntax;

public sealed partial class Parser
{
    // ------------------------------------------------------------ expressions

    public Expression ParseExpression() => ParseOr();

    /// <summary>Parses a standalone expression string (used by mapping-rule lifting).</summary>
    public static Expression ParseExpressionText(string text, LanguageProfile language, DiagnosticBag diagnostics)
    {
        var source = SourceText.From(text);
        var tokens = Lexer.Tokenize(source, language, diagnostics);
        var parser = new Parser(source, tokens, language, Array.Empty<VendorPattern>(), diagnostics);
        try
        {
            return parser.ParseExpression();
        }
        catch (ParseErrorException)
        {
            return new NameReference("<error>");
        }
    }

    private Expression ParseOr()
    {
        var left = ParseAnd();
        while (AtKeyword(Kw.Or) || Current.Kind is TokenKind.BarBar or TokenKind.Bar)
        {
            Advance();
            left = new BinaryExpression(BinaryOperator.Or, left, ParseAnd());
        }

        return left;
    }

    private Expression ParseAnd()
    {
        var left = ParseNot();
        while (AtKeyword(Kw.And) || Current.Kind is TokenKind.AmpAmp or TokenKind.Amp)
        {
            Advance();
            left = new BinaryExpression(BinaryOperator.And, left, ParseNot());
        }

        return left;
    }

    private Expression ParseNot()
    {
        if (AtKeyword(Kw.Not) || Current.Kind == TokenKind.Tilde)
        {
            Advance();
            return new UnaryExpression(UnaryOperator.Not, ParseNot());
        }

        return ParseComparison();
    }

    private Expression ParseComparison()
    {
        var left = ParseAdditive();
        var op = Current.Kind switch
        {
            TokenKind.Equals => BinaryOperator.Equal,
            TokenKind.EqualsEquals => BinaryOperator.Equal,
            TokenKind.NotEquals => BinaryOperator.NotEqual,
            TokenKind.Less => BinaryOperator.Less,
            TokenKind.LessOrEqual => BinaryOperator.LessOrEqual,
            TokenKind.Greater => BinaryOperator.Greater,
            TokenKind.GreaterOrEqual => BinaryOperator.GreaterOrEqual,
            _ => (BinaryOperator?)null,
        };

        if (op is null)
        {
            return left;
        }

        Advance();
        return new BinaryExpression(op.Value, left, ParseAdditive());
    }

    private Expression ParseAdditive()
    {
        var left = ParseTerm();
        while (Current.Kind is TokenKind.Plus or TokenKind.Minus)
        {
            var op = Advance().Kind == TokenKind.Plus ? BinaryOperator.Add : BinaryOperator.Subtract;
            left = new BinaryExpression(op, left, ParseTerm());
        }

        return left;
    }

    private Expression ParseTerm()
    {
        var left = ParseUnary();
        while (Current.Kind is TokenKind.Star or TokenKind.Slash)
        {
            var op = Advance().Kind == TokenKind.Star ? BinaryOperator.Multiply : BinaryOperator.Divide;
            left = new BinaryExpression(op, left, ParseUnary());
        }

        return left;
    }

    private Expression ParseUnary()
    {
        if (Current.Kind == TokenKind.Minus)
        {
            Advance();
            return new UnaryExpression(UnaryOperator.Negate, ParseUnary());
        }

        return ParsePrimary();
    }

    private Expression ParsePrimary()
    {
        switch (Current.Kind)
        {
            case TokenKind.Number:
                return new NumberLiteral(Advance().Text);
            case TokenKind.String:
                return new StringLiteral(Advance().Text);
            case TokenKind.OpenParen:
            {
                Advance();
                var inner = ParseExpression();
                Expect(TokenKind.CloseParen);
                return inner;
            }
            case TokenKind.Identifier:
            {
                if (AtKeyword(Kw.BoolTrue))
                {
                    Advance();
                    return new BoolLiteral(true);
                }

                if (AtKeyword(Kw.BoolFalse))
                {
                    Advance();
                    return new BoolLiteral(false);
                }

                return ParseTarget();
            }
            default:
                _diagnostics.Report(DiagnosticCodes.UnexpectedToken, Current.Span, Describe(Current), "an expression");
                throw new ParseErrorException();
        }
    }

    private Expression ParseTarget()
    {
        var name = ExpectIdentifier();
        if (Current.Kind == TokenKind.OpenBracket)
        {
            Advance();
            var index = ParseExpression();
            Expect(TokenKind.CloseBracket);
            return new IndexReference(name, index);
        }

        return new NameReference(name);
    }
}
