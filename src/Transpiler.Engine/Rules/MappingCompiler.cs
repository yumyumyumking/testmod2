namespace Transpiler.Engine.Rules;

/// <summary>
/// Pure tier-2 compilation of a language's <c>mappings</c> block: validates the rule
/// model and compiles lift patterns into the vendor recognizers the flat-language
/// parser runs.
/// </summary>
public static class MappingCompiler
{
    /// <summary>
    /// Structural validation of one mapping group: known selectors, and the fields
    /// each selector kind requires on both sides.
    /// </summary>
    public static List<string> Validate(MappingRule rule)
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(rule.Name))
        {
            problems.Add("rule is missing a 'name'.");
        }

        if (rule.Mappings.Count == 0)
        {
            problems.Add("rule declares no mappings.");
        }

        foreach (var mapping in rule.Mappings)
        {
            var label = string.IsNullOrEmpty(mapping.Id) ? $"selector '{mapping.Selector}'" : $"mapping '{mapping.Id}'";

            if (!MappingSelectors.All.Contains(mapping.Selector))
            {
                problems.Add($"{label}: unknown selector '{mapping.Selector}'. Known: {string.Join(", ", MappingSelectors.All)}.");
                continue;
            }

            if (MappingSelectors.IsFrame(mapping.Selector))
            {
                if (string.IsNullOrEmpty(mapping.Lower.Begin) || string.IsNullOrEmpty(mapping.Lower.Middle) || string.IsNullOrEmpty(mapping.Lower.End))
                {
                    problems.Add($"{label}: frame selectors require lower.begin, lower.middle and lower.end.");
                }

                if (string.IsNullOrEmpty(mapping.Lift.Begin) || string.IsNullOrEmpty(mapping.Lift.Middle) || string.IsNullOrEmpty(mapping.Lift.End))
                {
                    problems.Add($"{label}: frame selectors require lift.begin, lift.middle and lift.end.");
                }
            }
            else
            {
                if (string.IsNullOrEmpty(mapping.Lower.Format))
                {
                    problems.Add($"{label}: statement selectors require lower.format.");
                }

                if (string.IsNullOrEmpty(mapping.Lift.Pattern))
                {
                    problems.Add($"{label}: statement selectors require lift.pattern.");
                }
            }
        }

        return problems;
    }

    /// <summary>
    /// Compiles validated rules into the runtime set: per-selector winners for
    /// lowering, vendor lift recognizers for parsing. Invalid lift patterns are
    /// reported into <paramref name="errors"/> and skipped.
    /// </summary>
    public static MappingRuleSet Compile(IReadOnlyList<MappingRule> rules, List<string> errors)
    {
        var bySelector = new Dictionary<string, CompiledMapping>(StringComparer.Ordinal);
        var patterns = new List<VendorPattern>();

        foreach (var rule in rules)
        {
            foreach (var mapping in rule.Mappings)
            {
                var compiled = new CompiledMapping(rule, mapping);

                // First binding wins on a selector collision.
                if (!bySelector.ContainsKey(mapping.Selector))
                {
                    bySelector[mapping.Selector] = compiled;
                }

                try
                {
                    if (MappingSelectors.IsFrame(mapping.Selector))
                    {
                        patterns.Add(new VendorPattern(rule.Name, compiled.MappingId, MarkerRole.Begin, MarkerTemplate.CompilePattern(mapping.Lift.Begin!)));
                        patterns.Add(new VendorPattern(rule.Name, compiled.MappingId, MarkerRole.Middle, MarkerTemplate.CompilePattern(mapping.Lift.Middle!)));
                        patterns.Add(new VendorPattern(rule.Name, compiled.MappingId, MarkerRole.End, MarkerTemplate.CompilePattern(mapping.Lift.End!)));
                    }
                    else
                    {
                        patterns.Add(new VendorPattern(rule.Name, compiled.MappingId, MarkerRole.Statement, MarkerTemplate.CompilePattern(mapping.Lift.Pattern!)));
                    }
                }
                catch (ArgumentException ex)
                {
                    errors.Add($"rule '{rule.Name}': lift pattern in '{compiled.MappingId}' is invalid: {ex.Message}");
                }
            }
        }

        return new MappingRuleSet(rules, bySelector, patterns);
    }
}
