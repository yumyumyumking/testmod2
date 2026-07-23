namespace Transpiler.Engine.Syntax;

/// <summary>
/// Converts source text into tokens. Statements are line-oriented, so newlines are
/// significant and surface as <see cref="TokenKind.EndOfLine"/> tokens; a comment
/// (language line marker to end of line) is attached to the EOL token that terminates
/// its line. The lexer never throws: unknown characters become Bad tokens.
/// </summary>
public sealed class Lexer
{
    private readonly SourceText _text;
    private readonly LanguageProfile _language;
    private readonly DiagnosticBag _diagnostics;
    private int _position;

    public Lexer(SourceText text, LanguageProfile language, DiagnosticBag diagnostics)
    {
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _language = language ?? throw new ArgumentNullException(nameof(language));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    public static IReadOnlyList<Token> Tokenize(SourceText text, LanguageProfile language, DiagnosticBag diagnostics)
    {
        var lexer = new Lexer(text, language, diagnostics);
        var tokens = new List<Token>();
        Token token;
        do
        {
            token = lexer.Next();
            tokens.Add(token);
        }
        while (token.Kind != TokenKind.EndOfFile);
        return tokens;
    }

    private char Current => _position < _text.Length ? _text[_position] : '\0';

    private char Peek(int offset) => _position + offset < _text.Length ? _text[_position + offset] : '\0';

    private Token Next()
    {
        string? comment = null;

        // Skip horizontal whitespace and collect an optional trailing comment.
        while (true)
        {
            if (Current is ' ' or '\t' or '\r')
            {
                _position++;
                continue;
            }

            if (AtCommentMarker())
            {
                comment = ReadComment();
                continue;
            }

            break;
        }

        var start = _position;

        if (_position >= _text.Length)
        {
            return new Token(TokenKind.EndOfFile, string.Empty, new TextSpan(start, 0), comment);
        }

        if (Current == '\n')
        {
            _position++;
            return new Token(TokenKind.EndOfLine, "\n", new TextSpan(start, 1), comment);
        }

        if (char.IsLetter(Current) || Current == '_')
        {
            while (char.IsLetterOrDigit(Current) || Current == '_')
            {
                _position++;
            }

            var span = TextSpan.FromBounds(start, _position);
            return new Token(TokenKind.Identifier, _text.ToString(span), span);
        }

        if (char.IsDigit(Current))
        {
            while (char.IsDigit(Current))
            {
                _position++;
            }

            if (Current == '.' && char.IsDigit(Peek(1)))
            {
                _position++;
                while (char.IsDigit(Current))
                {
                    _position++;
                }
            }

            var span = TextSpan.FromBounds(start, _position);
            return new Token(TokenKind.Number, _text.ToString(span), span);
        }

        if (Current == '"')
        {
            _position++;
            while (Current != '"' && Current != '\n' && _position < _text.Length)
            {
                _position++;
            }

            if (Current == '"')
            {
                _position++;
                var span = TextSpan.FromBounds(start, _position);
                return new Token(TokenKind.String, _text.ToString(span), span);
            }

            var badSpan = TextSpan.FromBounds(start, _position);
            _diagnostics.Report(DiagnosticCodes.UnterminatedString, badSpan);
            return new Token(TokenKind.Bad, _text.ToString(badSpan), badSpan);
        }

        switch (Current)
        {
            case ':': return Punct(TokenKind.Colon, 1);
            case '=':
                if (Peek(1) == '=') { return Punct(TokenKind.EqualsEquals, 2); }
                return Punct(TokenKind.Equals, 1);
            case '~':
                if (Peek(1) == '=') { return Punct(TokenKind.NotEquals, 2); }
                return Punct(TokenKind.Tilde, 1);
            case '&':
                if (Peek(1) == '&') { return Punct(TokenKind.AmpAmp, 2); }
                return Punct(TokenKind.Amp, 1);
            case '|':
                if (Peek(1) == '|') { return Punct(TokenKind.BarBar, 2); }
                return Punct(TokenKind.Bar, 1);
            case '<':
                if (Peek(1) == '>') { return Punct(TokenKind.NotEquals, 2); }
                if (Peek(1) == '=') { return Punct(TokenKind.LessOrEqual, 2); }
                return Punct(TokenKind.Less, 1);
            case '>':
                if (Peek(1) == '=') { return Punct(TokenKind.GreaterOrEqual, 2); }
                return Punct(TokenKind.Greater, 1);
            case '+': return Punct(TokenKind.Plus, 1);
            case '-': return Punct(TokenKind.Minus, 1);
            case '*': return Punct(TokenKind.Star, 1);
            case '/': return Punct(TokenKind.Slash, 1);
            case '(': return Punct(TokenKind.OpenParen, 1);
            case ')': return Punct(TokenKind.CloseParen, 1);
            case '[': return Punct(TokenKind.OpenBracket, 1);
            case ']': return Punct(TokenKind.CloseBracket, 1);
            case '{': return Punct(TokenKind.OpenBrace, 1);
            case '}': return Punct(TokenKind.CloseBrace, 1);
            case ',': return Punct(TokenKind.Comma, 1);
            case ';': return Punct(TokenKind.Semicolon, 1);
            case '.': return Punct(TokenKind.Dot, 1);
            case '!': return Punct(TokenKind.Bang, 1);
        }

        var bad = TextSpan.FromBounds(start, start + 1);
        _diagnostics.Report(DiagnosticCodes.UnrecognizedCharacter, bad, Current);
        _position++;
        return new Token(TokenKind.Bad, _text.ToString(bad), bad);
    }

    private Token Punct(TokenKind kind, int length)
    {
        var span = new TextSpan(_position, length);
        var text = _text.ToString(span);
        _position += length;
        return new Token(kind, text, span);
    }

    private bool AtCommentMarker()
    {
        var marker = _language.Comment.Line;
        if (string.IsNullOrEmpty(marker) || _position + marker.Length > _text.Length)
        {
            return false;
        }

        for (var i = 0; i < marker.Length; i++)
        {
            if (_text[_position + i] != marker[i])
            {
                return false;
            }
        }

        return true;
    }

    private string ReadComment()
    {
        _position += _language.Comment.Line.Length;
        var start = _position;
        while (Current != '\n' && _position < _text.Length)
        {
            _position++;
        }

        return _text.ToString(TextSpan.FromBounds(start, _position)).Trim();
    }
}
