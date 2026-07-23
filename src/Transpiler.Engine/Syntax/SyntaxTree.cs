using System.Text.RegularExpressions;

namespace Transpiler.Engine.Syntax;

/// <summary>The parsed document: root node, source, language, and parse diagnostics.</summary>
public sealed class SyntaxTree
{
    public SyntaxTree(ProgramSyntax root, SourceText text, string languageName, IReadOnlyList<Diagnostic> diagnostics)
    {
        Root = root;
        Text = text;
        LanguageName = languageName;
        Diagnostics = diagnostics;
    }

    public ProgramSyntax Root { get; }

    public SourceText Text { get; }

    /// <summary>The language name this tree was parsed with.</summary>
    public string LanguageName { get; }

    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    /// <summary>
    /// Parses <paramref name="text"/> using the given language. <paramref name="vendorPatterns"/>
    /// lets the CL parser recognize mapping-rule marker lines; pass an empty list for CLX.
    /// When a <paramref name="diagnostics"/> sink is supplied, parse problems are reported
    /// into it (the compilation-wide sink); otherwise a private bag is used and exposed via
    /// <see cref="Diagnostics"/>.
    /// </summary>
    public static SyntaxTree Parse(
        SourceText text,
        LanguageProfile language,
        IReadOnlyList<VendorPattern> vendorPatterns,
        DiagnosticBag? diagnostics = null)
    {
        var bag = diagnostics ?? new DiagnosticBag(text);
        var tokens = Lexer.Tokenize(text, language, bag);
        var parser = new Parser(text, tokens, language, vendorPatterns, bag);
        var root = parser.ParseProgram();
        return new SyntaxTree(root, text, language.Name, bag.ToList());
    }
}
