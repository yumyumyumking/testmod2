namespace Transpiler.Engine;

/// <summary>
/// Everything configuration-driven that a transpilation run needs: the language
/// registry (tier 1, keyed by language <b>name</b> — drop a JSON file in and a new
/// language exists), each language's own tier-2 mappings, and load problems to
/// surface in the console. One language file is the whole configuration; there is no
/// shared rule tier.
/// </summary>
public sealed class TranspilerWorkspace
{
    private readonly IReadOnlyDictionary<string, LanguageProfile> _languages;

    public TranspilerWorkspace(
        IReadOnlyDictionary<string, LanguageProfile> languages,
        IReadOnlyList<string> errors,
        int skippedDisabled)
    {
        _languages = languages;
        Errors = errors;
        SkippedDisabled = skippedDisabled;
    }

    /// <summary>
    /// The tier-2 set for one language: its own <c>mappings</c> block, compiled once
    /// per profile (cached by <see cref="LanguageMappingCompiler"/>, so background
    /// editor transpiles share the work).
    /// </summary>
    public MappingRuleSet RulesFor(LanguageProfile language) => LanguageMappingCompiler.For(language);

    /// <summary>Load problems (invalid files are dropped with a reason, never fatal).</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>Files skipped because isEnabled was false.</summary>
    public int SkippedDisabled { get; }

    /// <summary>
    /// Every usable language name: the loaded registry plus the built-in CL/CLX
    /// fallbacks when files for them are absent. Sorted, case-insensitive.
    /// </summary>
    public IReadOnlyList<string> LanguageNames
    {
        get
        {
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in _languages.Keys)
            {
                names.Add(name);
            }

            names.Add(LanguageProfile.DefaultCl.Name);
            names.Add(LanguageProfile.DefaultClx.Name);
            return names.ToList();
        }
    }

    /// <summary>
    /// Strict lookup used by the engine: loaded registry first, then the built-in
    /// CL/CLX fallbacks. False means the language is simply not known.
    /// </summary>
    public bool TryGetLanguage(string name, out LanguageProfile profile)
    {
        if (_languages.TryGetValue(name, out profile!))
        {
            return true;
        }

        if (string.Equals(name, LanguageProfile.DefaultCl.Name, StringComparison.OrdinalIgnoreCase))
        {
            profile = LanguageProfile.DefaultCl;
            return true;
        }

        if (string.Equals(name, LanguageProfile.DefaultClx.Name, StringComparison.OrdinalIgnoreCase))
        {
            profile = LanguageProfile.DefaultClx;
            return true;
        }

        profile = null!;
        return false;
    }

    /// <summary>
    /// Lenient lookup for UI conveniences (completion word lists, previews): unknown
    /// names fall back to the built-in CLX profile rather than failing.
    /// </summary>
    public LanguageProfile Language(string name) =>
        TryGetLanguage(name, out var profile) ? profile : LanguageProfile.DefaultClx;

    /// <summary>
    /// The vendor lift patterns a parse of <paramref name="language"/> needs: flat
    /// languages carry tier-2 marker lines (from their own mappings block),
    /// structured ones never do.
    /// </summary>
    public IReadOnlyList<VendorPattern> PatternsFor(LanguageProfile language) =>
        PipelineFactory.NeedsVendorPatterns(language) ? RulesFor(language).VendorPatterns : Array.Empty<VendorPattern>();

    /// <summary>
    /// The file-extension convention shared by the editor and the batch UI:
    /// ".&lt;lowercased language name&gt;" (CLX → .clx).
    /// </summary>
    public static string DefaultExtensionFor(string languageName) =>
        "." + languageName.ToLowerInvariant();

    /// <summary>Built-in languages only — used by tests and as a last resort.</summary>
    public static TranspilerWorkspace CreateBuiltIn() => new(
        new Dictionary<string, LanguageProfile>(StringComparer.OrdinalIgnoreCase),
        Array.Empty<string>(),
        0);
}
