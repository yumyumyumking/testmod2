namespace Transpiler.Core.Languages;

/// <summary>
/// A language's program skeleton as authored in JSON (the <c>recipe</c> block): named
/// sections in declaration order, each declaring its content kind, header/terminator
/// spelling, presence and containment. The recipe is the only section configuration —
/// every language declares one, and it builds the internal <see cref="SectionPlan"/>
/// the parser interprets.
///
/// The recipe governs the program <b>skeleton</b> only — which sections a file is made
/// of and how their header lines are spelled. Statement syntax stays in
/// <c>keywords</c>/<c>capabilities</c>: that boundary is what makes the scheme generic
/// over control-style languages without pretending to host a C# grammar.
/// </summary>
public sealed class LanguageRecipe
{
    public LanguageRecipe(IReadOnlyList<KeyValuePair<string, RecipeSection>> entries)
    {
        Entries = entries;
    }

    /// <summary>The sections by JSON name, in declaration order (recognition priority).</summary>
    public IReadOnlyList<KeyValuePair<string, RecipeSection>> Entries { get; }

    /// <summary>
    /// Resolves a section's content kind: the explicit <see cref="RecipeSection.Kind"/>
    /// field, else the section's own name when that is a kind alias; null when neither
    /// resolves (a load-time validation error).
    /// </summary>
    public static SectionContent? ResolveKind(string sectionName, RecipeSection section) =>
        section.Kind is not null ? KindFromAlias(section.Kind) : KindFromAlias(sectionName);

    /// <summary>
    /// The built-in content kinds and their accepted JSON spellings. Aliases keep the
    /// natural names writable ("variabledeclaration", "module", "phase", …). Sections
    /// are generic in name and syntax, not semantics: each must map onto a kind the
    /// engine knows how to bind, transform and emit — that is what keeps the AST a
    /// language-neutral IR.
    /// </summary>
    public static SectionContent? KindFromAlias(string? alias) => alias?.ToLowerInvariant() switch
    {
        "namespace" or "file" or "module" => SectionContent.Namespace,
        "variabledeclaration" or "declarations" or "declaration" or "vars" => SectionContent.Declarations,
        "mainroutine" or "phase" => SectionContent.MainRoutine,
        "subroutine" or "routine" or "step" => SectionContent.SubRoutine,
        "function" => SectionContent.Function,
        "handler" => SectionContent.Handler,
        "statements" or "body" => SectionContent.Statements,
        _ => null,
    };

    /// <summary>Canonical kind names for diagnostics ("known kinds: …").</summary>
    public static readonly IReadOnlyList<string> KnownKinds = new[]
    {
        "namespace", "variabledeclaration", "mainroutine", "subroutine", "function", "handler", "statements",
    };
}
