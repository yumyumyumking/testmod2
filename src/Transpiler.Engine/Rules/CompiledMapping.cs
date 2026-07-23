namespace Transpiler.Engine.Rules;

/// <summary>One compiled, loaded mapping keyed by selector.</summary>
public sealed class CompiledMapping
{
    public CompiledMapping(MappingRule rule, RuleMapping mapping)
    {
        Rule = rule;
        Mapping = mapping;
    }

    public MappingRule Rule { get; }

    public RuleMapping Mapping { get; }

    public string MappingId => string.IsNullOrEmpty(Mapping.Id) ? Mapping.Selector : Mapping.Id;
}
