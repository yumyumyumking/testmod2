namespace Transpiler.Engine.Rules;

/// <summary>
/// One language's compiled mappings: selector lookup for lowering, vendor patterns
/// for the CL parser, and selector lookup by (rule, mapping id) for lift assembly.
/// </summary>
public sealed class MappingRuleSet
{
    public static readonly MappingRuleSet Empty = new(
        Array.Empty<MappingRule>(), new Dictionary<string, CompiledMapping>(StringComparer.Ordinal), Array.Empty<VendorPattern>());

    public MappingRuleSet(
        IReadOnlyList<MappingRule> rules,
        IReadOnlyDictionary<string, CompiledMapping> bySelector,
        IReadOnlyList<VendorPattern> vendorPatterns)
    {
        _bySelector = bySelector;
        VendorPatterns = vendorPatterns;

        // Lift assembly resolves markers by (rule, mapping id): parsed marker lines
        // carry both, and this index maps them back to the selector whose AST node
        // they assemble into. First entry wins on a collision.
        var selectorByMarker = new Dictionary<(string Rule, string Id), string>();
        foreach (var rule in rules)
        {
            foreach (var mapping in rule.Mappings)
            {
                var compiled = new CompiledMapping(rule, mapping);
                selectorByMarker.TryAdd((rule.Name, compiled.MappingId), mapping.Selector);
            }
        }

        _selectorByMarker = selectorByMarker;
    }

    private readonly IReadOnlyDictionary<(string Rule, string Id), string> _selectorByMarker;

    /// <summary>The winning mapping per selector.</summary>
    private readonly IReadOnlyDictionary<string, CompiledMapping> _bySelector;

    /// <summary>Lift recognizers handed to the CL parser.</summary>
    public IReadOnlyList<VendorPattern> VendorPatterns { get; }

    public CompiledMapping? Find(string selector) =>
        _bySelector.TryGetValue(selector, out var mapping) ? mapping : null;

    public string? SelectorFor(string ruleName, string mappingId) =>
        _selectorByMarker.TryGetValue((ruleName, mappingId), out var selector) ? selector : null;
}
