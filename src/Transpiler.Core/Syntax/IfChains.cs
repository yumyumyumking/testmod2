namespace Transpiler.Core.Syntax;

/// <summary>
/// Canonicalization of IF shapes (SPEC §8.3): an ELSE consisting of exactly one IF
/// block folds into the ELSIF chain, so nested-else and elsif spellings are the same
/// tree. Lives in Syntax because it is an AST identity, used by lowering (per-level
/// end labels), lifting (Finish canonicalization) and the round-trip dumper alike.
/// </summary>
public static class IfChains
{
    public static IfBlockStatement Normalize(IfBlockStatement node)
    {
        var branches = new List<IfBranch>(node.Branches);
        var elseBody = node.ElseBody;
        while (elseBody is { Count: 1 } && elseBody[0] is IfBlockStatement inner)
        {
            branches.AddRange(inner.Branches);
            elseBody = inner.ElseBody;
        }

        return new IfBlockStatement(branches, elseBody)
        {
            Span = node.Span,
            LeadingComments = node.LeadingComments,
            TrailingComment = node.TrailingComment,
        };
    }
}
