using System.Runtime.CompilerServices;

namespace Transpiler.Engine.Rules;

/// <summary>
/// Compiles a language's own <c>mappings</c> block (its declared vendor-construct
/// correspondences) into a runtime <see cref="MappingRuleSet"/>, cached per profile —
/// loaded once at startup, like the section plan. The loader forces this during
/// validation, so a failure reaching parse time means a hand-built (unvalidated)
/// profile; the exception message still names the mapping and pattern.
/// </summary>
public static class LanguageMappingCompiler
{
    private static readonly ConditionalWeakTable<LanguageProfile, MappingRuleSet> Cache = new();

    /// <summary>The compiled set of the language's own mappings; empty when it declares none.</summary>
    public static MappingRuleSet For(LanguageProfile language) => Cache.GetValue(language, Build);

    /// <summary>
    /// The language's <c>mappings</c> block as one tier-2 rule: rule name = language
    /// name, one <see cref="RuleMapping"/> per entry with the paired lowering/lifting
    /// sides.
    /// </summary>
    public static MappingRule ToRule(LanguageProfile language) => new()
    {
        Name = language.Name,
        Mappings = (language.DeclaredMappings ?? new Dictionary<string, LanguageMapping>())
            .Select(static entry => new RuleMapping
            {
                Id = entry.Key,
                Selector = entry.Value.Selector,
                Lower = new LowerSpec
                {
                    Begin = entry.Value.Lowering.Begin,
                    Middle = entry.Value.Lowering.Middle,
                    End = entry.Value.Lowering.End,
                    Format = entry.Value.Lowering.Format,
                },
                Lift = new LiftSpec
                {
                    Begin = entry.Value.Lifting.Begin,
                    Middle = entry.Value.Lifting.Middle,
                    End = entry.Value.Lifting.End,
                    Pattern = entry.Value.Lifting.Pattern,
                },
            })
            .ToList(),
    };

    private static MappingRuleSet Build(LanguageProfile language)
    {
        if (language.DeclaredMappings is not { Count: > 0 })
        {
            return MappingRuleSet.Empty;
        }

        // Structural validation first: a hand-built profile with a missing side must
        // fail with a named field, not a null dereference inside pattern compilation.
        var rule = ToRule(language);
        var errors = MappingCompiler.Validate(rule);
        MappingRuleSet? set = null;
        if (errors.Count == 0)
        {
            set = MappingCompiler.Compile(new[] { rule }, errors);
        }

        if (errors.Count > 0 || set is null)
        {
            throw new InvalidOperationException($"mappings of language '{language.Name}': {errors[0]}");
        }

        return set;
    }
}
