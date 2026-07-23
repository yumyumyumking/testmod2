namespace Transpiler.Core.Syntax;

/// <summary>
/// One main-routine section instance (what CL spells PHASE), holding its
/// sub-routines in order — the block a controller's scan cycle executes.
/// </summary>
public sealed class MainRoutine : SyntaxNode
{
    public MainRoutine(string name, IReadOnlyList<SubRoutine> subRoutines)
    {
        Name = name;
        SubRoutines = subRoutines;
    }

    public string Name { get; }

    public IReadOnlyList<SubRoutine> SubRoutines { get; }

    /// <summary>Recipe-pattern header captures beyond the name; empty for keyword-parsed headers.</summary>
    public IReadOnlyDictionary<string, string> ExtraCaptures { get; init; } = NoCaptures;

    public MainRoutine WithSubRoutines(IReadOnlyList<SubRoutine> subRoutines) =>
        new(Name, subRoutines)
        {
            Span = Span,
            LeadingComments = LeadingComments,
            TrailingComment = TrailingComment,
            ExtraCaptures = ExtraCaptures,
        };
}
