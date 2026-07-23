namespace Transpiler.Core.Languages;

/// <summary>
/// One alternative of the program-level grammar: what the section holds
/// (<see cref="Content"/>), how its header line is spelled, and how often it may
/// appear (<see cref="Cardinality"/>). Two header spellings exist:
/// <list type="bullet">
/// <item><b>Keyword shorthand</b> — <see cref="Delimiters"/> start/end keywords; the
/// engine's built-in flexible header parse (identifier name, optional
/// <c>out = name(args)</c> form, brace blocks).</item>
/// <item><b>Typed-capture patterns</b> — <see cref="StartPattern"/> (and optionally
/// <see cref="EndPattern"/>) matched against the raw header line, with
/// <see cref="EmitTemplate"/>/<see cref="EndEmitTemplate"/> rendering the emit side.
/// Authored through a language's <c>recipe</c>.</item>
/// </list>
/// Keyword-less content (declarations, bare statements) uses neither
/// (<see cref="DelimiterPair.None"/>, no patterns).
/// </summary>
public sealed record SectionRule(
    SectionContent Content,
    DelimiterPair Delimiters,
    SectionCardinality Cardinality)
{
    /// <summary>The section's recipe name; empty for synthesized (header/legacy) rules.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Typed-capture pattern for the header line ("MODULE {name}"); null in keyword mode.</summary>
    public string? StartPattern { get; init; }

    /// <summary>Emit template for the header line; null in keyword mode.</summary>
    public string? EmitTemplate { get; init; }

    /// <summary>Typed-capture pattern for the terminator line; null when the end is a keyword or absent.</summary>
    public string? EndPattern { get; init; }

    /// <summary>Emit template for the terminator line; null → keyword-mode emission rules apply.</summary>
    public string? EndEmitTemplate { get; init; }

    /// <summary>
    /// Fallback values for emit-template placeholders the emitted program does not
    /// carry (authored as the recipe section's <c>defaults</c>). Empty by default.
    /// </summary>
    public IReadOnlyDictionary<string, string> Defaults { get; init; } = SyntaxNode.NoCaptures;

    /// <summary>Terminator tolerated as absent on input while still emitted on output.</summary>
    public bool EndOptional { get; init; }

    /// <summary>
    /// The leading literal keyword of <see cref="StartPattern"/> — what the parser's
    /// boundary predicates dispatch on before running the full pattern.
    /// </summary>
    public string? StartFirstWord { get; init; }

    /// <summary>The leading literal keyword of <see cref="EndPattern"/>.</summary>
    public string? EndFirstWord { get; init; }

    /// <summary>True when the header line is pattern-matched rather than keyword-parsed.</summary>
    public bool UsesPatterns => StartPattern is not null;

    /// <summary>True when the section is closed by an explicit terminator (keyword or pattern).</summary>
    public bool HasTerminator => Delimiters.HasTerminator || EndPattern is not null;

    /// <summary>True for rules that produce a named routine or routine container.</summary>
    public bool IsRoutine => Content
        is SectionContent.MainRoutine
        or SectionContent.SubRoutine
        or SectionContent.Function
        or SectionContent.Handler;

    /// <summary>
    /// The <see cref="SectionSlots"/> tag stored on routines parsed from this rule
    /// (<see cref="Core.Syntax.SubRoutine.Kind"/>).
    /// </summary>
    public string RoutineKind => Content switch
    {
        SectionContent.Function => SectionSlots.Function,
        SectionContent.Handler => SectionSlots.Handler,
        _ => SectionSlots.SubRoutine,
    };

    /// <summary>The section's display name for diagnostics: recipe name, else its start spelling.</summary>
    public string DisplayName =>
        Name.Length > 0 ? Name : Delimiters.Start ?? Content.ToString();
}
