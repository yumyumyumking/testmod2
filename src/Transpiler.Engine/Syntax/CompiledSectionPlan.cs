using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Transpiler.Engine.Syntax;

/// <summary>
/// The compiled form of a language's <see cref="SectionPlan"/>: one regex pair per
/// pattern-mode rule, built once per plan and cached — "loaded on startup into a
/// state". The loader forces this compilation during validation, so a pattern that
/// cannot compile drops its language with the reason; reaching a failure here at
/// parse time therefore means a hand-built (unvalidated) profile, and the exception
/// message still names the section and pattern.
/// </summary>
internal sealed class CompiledSectionPlan
{
    private static readonly ConditionalWeakTable<SectionPlan, CompiledSectionPlan> Cache = new();

    private readonly Dictionary<SectionRule, CompiledSectionRule> _rules = new();

    private CompiledSectionPlan(SectionPlan plan, bool caseSensitive)
    {
        foreach (var rule in plan.FileSections.Concat(plan.MainRoutineSections))
        {
            Compile(rule, caseSensitive);
        }

        if (plan.Namespace is { } ns)
        {
            Compile(ns, caseSensitive);
        }
    }

    public static CompiledSectionPlan For(LanguageProfile language) =>
        Cache.GetValue(language.Plan, plan => new CompiledSectionPlan(plan, language.CaseSensitive));

    public CompiledSectionRule Rule(SectionRule rule) => _rules[rule];

    private void Compile(SectionRule rule, bool caseSensitive)
    {
        if (_rules.ContainsKey(rule))
        {
            return;
        }

        try
        {
            _rules[rule] = new CompiledSectionRule(
                rule.StartPattern is null ? null : SectionPatterns.CompileHeader(rule.StartPattern, caseSensitive),
                rule.EndPattern is null ? null : SectionPatterns.CompileTerminator(rule.EndPattern, caseSensitive));
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(
                $"recipe.{rule.DisplayName}: pattern does not compile: {ex.Message}");
        }
    }
}

/// <summary>Compiled regexes of one pattern-mode section rule (null in keyword mode).</summary>
internal sealed record CompiledSectionRule(Regex? Start, Regex? End);
