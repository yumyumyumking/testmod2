namespace Transpiler.Engine.Rules;

/// <summary>Lift-side recognizers with typed captures, e.g. "ALLOC {name:identifier} SIZE {size:number}".</summary>
public sealed class LiftSpec
{
    public string? Begin { get; init; }

    public string? Middle { get; init; }

    public string? End { get; init; }

    public string? Pattern { get; init; }
}
