namespace Transpiler.Engine.Rules;

/// <summary>
/// A named group of tier-2 mappings in compiled form — the runtime carrier built from
/// a language's <c>mappings</c> block (rule name = language name). Emitted marker
/// lines embed the rule name and mapping id, so lift assembly can resolve them back
/// through <see cref="MappingRuleSet.SelectorFor"/>.
/// </summary>
public sealed class MappingRule
{
    public string Name { get; init; } = string.Empty;

    public IReadOnlyList<RuleMapping> Mappings { get; init; } = Array.Empty<RuleMapping>();
}
