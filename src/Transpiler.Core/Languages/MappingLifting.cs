namespace Transpiler.Core.Languages;

/// <summary>
/// The lifting side of a <see cref="LanguageMapping"/>: typed-capture patterns (the
/// same dialect as recipe formats) recognizing the construct's lines in this language
/// so they can be reassembled into the IR. Frame selectors use
/// <see cref="Begin"/>/<see cref="Middle"/>/<see cref="End"/>; statement selectors use
/// the single-line <see cref="Pattern"/>. Captures must include what the selector
/// needs to rebuild the node (e.g. <c>{faultVar:identifier}</c>,
/// <c>{index:expression}</c>).
/// </summary>
public sealed record MappingLifting
{
    public string? Begin { get; init; }

    public string? Middle { get; init; }

    public string? End { get; init; }

    public string? Pattern { get; init; }
}
