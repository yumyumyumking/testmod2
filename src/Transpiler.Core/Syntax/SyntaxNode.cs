namespace Transpiler.Core.Syntax;

/// <summary>
/// Base of every node. Nodes are immutable; transformation passes build new nodes.
/// Comment properties carry the source comments attached to the construct so the
/// emitters can preserve them (comment-preserving, format-normalizing model).
/// </summary>
public abstract class SyntaxNode
{
    /// <summary>Shared empty capture map for nodes without recipe-pattern extras.</summary>
    public static readonly IReadOnlyDictionary<string, string> NoCaptures =
        new Dictionary<string, string>();

    public TextSpan Span { get; init; }

    public IReadOnlyList<string> LeadingComments { get; init; } = Array.Empty<string>();

    public string? TrailingComment { get; init; }
}
