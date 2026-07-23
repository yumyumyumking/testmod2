namespace Transpiler.Core.Syntax;

/// <summary>
/// One routine block: a sub-routine inside a main routine (the default — what CL
/// spells STEP inside PHASE), or a file-level FUNCTION / HANDLER routine in
/// languages that have those sections. Labels are scoped per routine.
/// </summary>
public sealed class SubRoutine : SyntaxNode
{
    public SubRoutine(string name, IReadOnlyList<Statement> body, string kind = SectionSlots.SubRoutine)
    {
        Name = name;
        Body = body;
        Kind = kind;
    }

    public string Name { get; }

    public IReadOnlyList<Statement> Body { get; }

    /// <summary>
    /// Which section slot produced this routine — a <see cref="SectionSlots"/> name
    /// (SubRoutine by default; Function or Handler for file-level routines). String-keyed
    /// like the section configuration itself, so new slot kinds need no code change here.
    /// </summary>
    public string Kind { get; }

    /// <summary>Recipe-pattern header captures beyond the name; empty for keyword-parsed routines.</summary>
    public IReadOnlyDictionary<string, string> ExtraCaptures { get; init; } = NoCaptures;

    public SubRoutine WithBody(IReadOnlyList<Statement> body) =>
        new(Name, body, Kind)
        {
            Span = Span,
            LeadingComments = LeadingComments,
            TrailingComment = TrailingComment,
            ExtraCaptures = ExtraCaptures,
        };
}
