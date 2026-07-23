namespace Transpiler.Core.Languages;

/// <summary>
/// One section of a language recipe, as authored in JSON. The section's <b>name</b> is
/// its key in the <c>recipe</c> object; this record holds everything else:
///
/// <list type="bullet">
/// <item><see cref="Kind"/> — which built-in content the section carries. Sections are
/// generic in name and syntax, not in semantics: the transpiler must know what to bind,
/// transform and emit, so every section maps onto one of the engine's content kinds
/// (namespace / variabledeclaration / mainroutine / subroutine / function / handler /
/// statements — see <see cref="LanguageRecipe.KindFromAlias"/>). When the section's
/// name already spells a kind or one of its aliases, the field may be omitted.</item>
/// <item>Two header spellings, at most one of which may be used:
/// <see cref="Start"/>/<see cref="End"/> keyword shorthand (the engine's built-in
/// flexible header parse — identifier name, optional <c>out = name(args)</c> form,
/// brace blocks), or <see cref="Format"/>/<see cref="Emit"/> typed-capture patterns
/// for shaped headers ("SEQUENCE {name} ({hardware}; POINT {database})").
/// Declarations/statements kinds may be markerless (neither spelling): they are
/// recognized by their content.</item>
/// <item>Terminator: <see cref="End"/> keyword or <see cref="EndFormat"/> pattern;
/// <see cref="EndEmit"/> template controls what is written (defaults to the bare end
/// keyword); <see cref="EndOptional"/> tolerates a missing terminator on input while
/// still emitting one.</item>
/// <item>Containment: <see cref="MustContain"/> (child appears at least once per
/// instance of this section) and <see cref="CanContain"/> (child may appear any number
/// of times). A known section encountered in a host whose lists do not include it is a
/// compile error (CLX0116); an unknown line stays CLX0103.</item>
/// <item><see cref="Presence"/> ("required"/"optional") — how often the section must
/// appear at its host. For hosted sections this is the same fact as being listed in
/// the host's <c>mustContain</c>, so it may be omitted; when both are written they
/// must agree (the loader reports which side to fix).</item>
/// </list>
/// </summary>
public sealed record RecipeSection
{
    /// <summary>Built-in content kind, or null when the section name is itself a kind alias.</summary>
    public string? Kind { get; init; }

    /// <summary>"required" or "optional"; null derives from the host's containment lists.</summary>
    public string? Presence { get; init; }

    /// <summary>Keyword shorthand: the section's start keyword (built-in header parse).</summary>
    public string? Start { get; init; }

    /// <summary>Keyword shorthand: the section's end keyword (optional trailing name tolerated).</summary>
    public string? End { get; init; }

    /// <summary>Typed-capture pattern for the header line, e.g. "MODULE {name}".</summary>
    public string? Format { get; init; }

    /// <summary>
    /// Emit template for the header line ({capture} placeholders). Defaults to
    /// <see cref="Format"/> with the type annotations stripped.
    /// </summary>
    public string? Emit { get; init; }

    /// <summary>Typed-capture pattern for the terminator line (alternative to <see cref="End"/>).</summary>
    public string? EndFormat { get; init; }

    /// <summary>Emit template for the terminator line. Defaults to the bare end keyword.</summary>
    public string? EndEmit { get; init; }

    /// <summary>Terminator tolerated as absent on input (still emitted on output).</summary>
    public bool EndOptional { get; init; }

    /// <summary>
    /// Values for <see cref="Format"/> captures when the program being emitted does
    /// not carry them — a source language whose header never captured the field, or a
    /// synthesized header. Keys must be captures of this section's format. This keeps
    /// field semantics entirely in the language file: the engine fills, it never
    /// invents.
    /// </summary>
    public IReadOnlyDictionary<string, string> Defaults { get; init; } = EmptyDefaults;

    public IReadOnlyList<string> MustContain { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> CanContain { get; init; } = Array.Empty<string>();

    private static readonly IReadOnlyDictionary<string, string> EmptyDefaults =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
