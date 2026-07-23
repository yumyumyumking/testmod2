namespace Transpiler.Core.Syntax;

public enum TokenKind
{
    Identifier,
    Number,
    String,
    Colon,
    Equals,
    EqualsEquals,   // ==  (equality; alias of Equals in a comparison)
    NotEquals,      // <>  or ~=
    Less,
    LessOrEqual,
    Greater,
    GreaterOrEqual,
    Plus,
    Minus,
    Star,
    Slash,
    OpenParen,
    CloseParen,
    OpenBracket,
    CloseBracket,
    OpenBrace,
    CloseBrace,
    Comma,
    Semicolon,
    Dot,
    Bang,
    Tilde,          // ~   (logical NOT; language-configurable)
    AmpAmp,         // &&
    BarBar,         // ||
    Amp,            // &
    Bar,            // |
    EndOfLine,
    EndOfFile,
    Bad,
}
