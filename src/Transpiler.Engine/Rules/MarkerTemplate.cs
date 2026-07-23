using System.Text.RegularExpressions;

namespace Transpiler.Engine.Rules;

/// <summary>
/// The typed-capture pattern dialect: "ALLOC {name:identifier} SIZE {size:number}".
/// One dialect serves the whole configuration surface — tier-2 lift patterns here,
/// and the section-header patterns of language recipes
/// (<see cref="Transpiler.Engine.Syntax.SectionPatterns"/>) — so authors learn it once.
/// </summary>
public static class MarkerTemplate
{
    private static readonly Regex Placeholder = new(@"\{(\w+)(?::(\w+))?\}", RegexOptions.CultureInvariant);

    internal static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Renders a template by substituting {capture} placeholders.</summary>
    public static string Render(string template, IReadOnlyDictionary<string, string> captures) =>
        Placeholder.Replace(template, match =>
            captures.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value);

    /// <summary>
    /// Compiles a lift pattern with typed captures into an anchored regex:
    /// identifier → \w+, number → digits, expression → lazy any (sub-parsed later).
    /// </summary>
    public static Regex CompilePattern(string pattern) => new(
        @"^\s*" + TranslateCore(pattern) + @"\s*$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        MatchTimeout);

    /// <summary>The placeholder names a pattern or template mentions, in order.</summary>
    public static IReadOnlyList<string> PlaceholderNames(string patternOrTemplate) =>
        Placeholder.Matches(patternOrTemplate).Select(static m => m.Groups[1].Value).ToList();

    /// <summary>
    /// Translates a typed-capture pattern into unanchored regex text — the shared core
    /// of <see cref="CompilePattern"/> and the section-pattern compiler.
    /// </summary>
    internal static string TranslateCore(string pattern)
    {
        var escaped = Regex.Escape(pattern).Replace(@"\{", "{").Replace(@"\}", "}");

        // Tolerate flexible whitespace between pattern words.
        escaped = Regex.Replace(escaped, @"(\\ )+", @"\s+");

        return Placeholder.Replace(escaped, match =>
        {
            var name = match.Groups[1].Value;
            var type = match.Groups[2].Success ? match.Groups[2].Value : "identifier";
            return type.ToLowerInvariant() switch
            {
                "number" => $@"(?<{name}>\d+(?:\.\d+)?)",
                "expression" => $@"(?<{name}>.+?)",
                // Mirrors the lexer's identifier rule: digit-leading tokens are not
                // identifiers and must not silently pass as one.
                _ => $@"(?<{name}>[A-Za-z_]\w*)",
            };
        });
    }
}
