namespace Transpiler.Core.Languages;

/// <summary>
/// The lowering side of a <see cref="LanguageMapping"/>: templates rendering the
/// construct into this language. Frame selectors (TryBlock) use
/// <see cref="Begin"/>/<see cref="Middle"/>/<see cref="End"/>; statement selectors use
/// the single-line <see cref="Format"/>. Placeholders are the selector's captures
/// (e.g. <c>{faultVar}</c>, <c>{name}</c>/<c>{size}</c>).
/// </summary>
public sealed record MappingLowering
{
    public string? Begin { get; init; }

    public string? Middle { get; init; }

    public string? End { get; init; }

    public string? Format { get; init; }
}
