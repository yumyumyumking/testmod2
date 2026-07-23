using System.Text.RegularExpressions;

namespace Transpiler.Engine.Syntax;

/// <summary>
/// Compilation of a recipe section's typed-capture patterns (the same dialect as
/// tier-2 lift patterns — see <see cref="Rules.MarkerTemplate"/>) into anchored,
/// timeout-bound line regexes, plus the template rendering of the emit side.
/// Exactly one capture name carries engine semantics: <c>name</c>, the section
/// instance's identity. Every other capture is an opaque field — stored on the
/// parsed node, reproduced by the emit template, and (when the emitted program does
/// not carry it) filled from the section's <c>defaults</c>.
/// </summary>
public static class SectionPatterns
{
    /// <summary>Capture name for a section instance's name.</summary>
    public const string NameCapture = "name";

    /// <summary>Compiles a section header pattern into an anchored line regex.</summary>
    public static Regex CompileHeader(string pattern, bool caseSensitive) => new(
        @"^\s*" + Tolerant(Rules.MarkerTemplate.TranslateCore(pattern)) + @"\s*$",
        Options(caseSensitive),
        Rules.MarkerTemplate.MatchTimeout);

    /// <summary>
    /// Compiles a terminator pattern. A pattern without a {name} capture gets an
    /// optional trailing name appended — mirroring keyword terminators, where "END"
    /// also matches "END Monitor" and the name, when present, is checked against the
    /// section's (CLX0113 on mismatch).
    /// </summary>
    public static Regex CompileTerminator(string pattern, bool caseSensitive)
    {
        var core = Tolerant(Rules.MarkerTemplate.TranslateCore(pattern));
        if (!Rules.MarkerTemplate.PlaceholderNames(pattern).Contains(NameCapture, StringComparer.OrdinalIgnoreCase))
        {
            core += @"(?:\s+(?<" + NameCapture + @">[A-Za-z_]\w*))?";
        }

        return new Regex(@"^\s*" + core + @"\s*$", Options(caseSensitive), Rules.MarkerTemplate.MatchTimeout);
    }

    /// <summary>
    /// Header lines were token-parsed before recipes, so spacing around punctuation
    /// never mattered ("( PM ;POINT Db )" parsed fine). Compiled section patterns keep
    /// that tolerance: literal punctuation accepts optional whitespace on both sides.
    /// Only pattern-literal punctuation is touched — the group constructs the
    /// translator emits use bare parentheses, while literal ones arrive escaped.
    /// </summary>
    private static string Tolerant(string core)
    {
        core = core
            .Replace(@"\(", @"\s*\(\s*")
            .Replace(@"\)", @"\s*\)\s*")
            .Replace(";", @"\s*;\s*")
            .Replace(",", @"\s*,\s*");

        // Collapse required whitespace that now neighbours optional whitespace, so
        // "Demo (PM" and "Demo(PM", "; POINT" and ";POINT" all match alike.
        while (core.Contains(@"\s*\s+") || core.Contains(@"\s+\s*"))
        {
            core = core.Replace(@"\s*\s+", @"\s*").Replace(@"\s+\s*", @"\s*");
        }

        return core;
    }

    /// <summary>Renders an emit template from a capture map.</summary>
    public static string Render(string template, IReadOnlyDictionary<string, string> captures) =>
        Rules.MarkerTemplate.Render(template, captures);

    /// <summary>The placeholder names a template mentions.</summary>
    public static IReadOnlyList<string> Placeholders(string patternOrTemplate) =>
        Rules.MarkerTemplate.PlaceholderNames(patternOrTemplate);

    /// <summary>Human-readable expected form of a pattern for diagnostics: "{name:identifier}" → "&lt;name&gt;".</summary>
    public static string ExpectedForm(string pattern) =>
        Regex.Replace(pattern, @"\{(\w+)(?::\w+)?\}", "<$1>");

    /// <summary>The named captures of a successful match (numbered groups excluded).</summary>
    public static Dictionary<string, string> CapturesOf(Regex regex, Match match)
    {
        var captures = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var groupName in regex.GetGroupNames())
        {
            if (!int.TryParse(groupName, out _) && match.Groups[groupName].Success)
            {
                captures[groupName] = match.Groups[groupName].Value;
            }
        }

        return captures;
    }

    private static RegexOptions Options(bool caseSensitive) =>
        RegexOptions.CultureInvariant | (caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
}
