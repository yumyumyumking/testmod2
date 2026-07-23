namespace Transpiler.Engine.Rules;

public sealed class RuleMapping
{
    public string Id { get; init; } = string.Empty;

    public string Selector { get; init; } = string.Empty;

    public LowerSpec Lower { get; init; } = new();

    public LiftSpec Lift { get; init; } = new();
}
