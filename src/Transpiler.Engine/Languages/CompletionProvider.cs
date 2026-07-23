using System.Text.RegularExpressions;

namespace Transpiler.Engine.Languages;

/// <summary>
/// Editor completion candidates for one language: the profile's section delimiters
/// and keyword phrases, plus identifiers already present in the document. Language
/// knowledge lives here in the engine — testable without a UI and reusable by any
/// future front end — while the editor supplies only caret and popup mechanics.
/// </summary>
public static class CompletionProvider
{
    /// <summary>
    /// Case-insensitive prefix matches, in the language's spelling, excluding an
    /// exact match of the prefix itself. Empty prefix yields no candidates.
    /// </summary>
    public static IReadOnlyList<string> GetCompletions(LanguageProfile language, string documentText, string prefix)
    {
        if (string.IsNullOrEmpty(prefix))
        {
            return Array.Empty<string>();
        }

        var words = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        // Section spellings come from the plan: keyword delimiters verbatim, and the
        // literal words of recipe patterns (placeholders contribute nothing).
        var plan = language.Plan;
        foreach (var rule in plan.FileSections.Concat(plan.MainRoutineSections)
                     .Concat(plan.Namespace is { } ns ? new[] { ns } : Array.Empty<SectionRule>()))
        {
            AddPhrase(words, rule.Delimiters.Start);
            AddPhrase(words, rule.Delimiters.End);
            AddPatternLiterals(words, rule.StartPattern);
            AddPatternLiterals(words, rule.EndPattern);
        }

        foreach (var scope in language.Variables.Scopes)
        {
            AddPhrase(words, scope.Keyword);
        }

        foreach (var kind in language.Variables.Kinds)
        {
            AddPhrase(words, kind.Spelling);
            AddPhrase(words, kind.Point);
        }

        var kw = language.Keywords;
        var keywordPhrases = new[]
        {
            kw.If, kw.Then, kw.Else, kw.ElseIf, kw.EndIf,
            kw.While, kw.Do, kw.EndWhile, kw.Repeat, kw.Times, kw.EndRepeat,
            kw.Try, kw.Catch, kw.EndTry, kw.Array,
            kw.Set, kw.Reset, kw.Goto, kw.Call, kw.Return,
            kw.BoolTrue, kw.BoolFalse, kw.And, kw.Or, kw.Not,
        };

        foreach (var phrase in keywordPhrases)
        {
            AddPhrase(words, phrase);
        }

        foreach (Match match in Regex.Matches(documentText ?? string.Empty, @"[A-Za-z_]\w{2,}"))
        {
            words.Add(match.Value);
        }

        return words
            .Where(w => w.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(w, prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static void AddPhrase(SortedSet<string> words, string? phrase)
    {
        if (string.IsNullOrEmpty(phrase))
        {
            return; // absent section slots contribute nothing
        }

        foreach (var word in phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            words.Add(word);
        }
    }

    private static void AddPatternLiterals(SortedSet<string> words, string? pattern)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return;
        }

        foreach (var fragment in Regex.Split(pattern, @"\{\w+(?::\w+)?\}"))
        {
            foreach (Match word in Regex.Matches(fragment, @"[A-Za-z_]\w*"))
            {
                words.Add(word.Value);
            }
        }
    }
}
