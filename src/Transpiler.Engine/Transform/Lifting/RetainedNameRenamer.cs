namespace Transpiler.Engine.Transform.Lifting;

/// <summary>
/// Renames retained generated-prefix names in lifted output — labels (with their
/// GOTOs) and/or variables (with every reference). Needed because an isGenerated
/// flag does not survive re-parsing: the verifier re-binds the EMITTED text, so a
/// surviving "__CLX_…" name would trip the binder's prefix-collision check
/// (CLX2102). A <see cref="StatementRewriter"/> subclass: only the four name-bearing
/// hooks are overridden; traversal and rebuild-with-fidelity come from the base.
/// </summary>
internal sealed class RetainedNameRenamer : StatementRewriter
{
    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();

    private readonly IReadOnlyDictionary<string, string> _labels;
    private readonly IReadOnlyDictionary<string, string> _variables;

    private RetainedNameRenamer(
        IReadOnlyDictionary<string, string> labels,
        IReadOnlyDictionary<string, string> variables)
    {
        _labels = labels;
        _variables = variables;
    }

    /// <summary>Renamer for label names and their GOTO references (language-comparer map).</summary>
    public static RetainedNameRenamer ForLabels(IReadOnlyDictionary<string, string> map) => new(map, Empty);

    /// <summary>Renamer for variable names in every expression position (language-comparer map).</summary>
    public static RetainedNameRenamer ForVariables(IReadOnlyDictionary<string, string> map) => new(Empty, map);

    protected override Statement RewriteLabel(LabelStatement node) =>
        _labels.TryGetValue(node.Name, out var renamed)
            ? new LabelStatement(renamed)
            {
                Span = node.Span, LeadingComments = node.LeadingComments, TrailingComment = node.TrailingComment,
            }
            : node;

    protected override Statement RewriteGoto(GotoStatement node) =>
        _labels.TryGetValue(node.Label, out var renamed)
            ? new GotoStatement(renamed)
            {
                Span = node.Span, LeadingComments = node.LeadingComments, TrailingComment = node.TrailingComment,
            }
            : node;

    protected override Expression RewriteName(NameReference node) =>
        _variables.TryGetValue(node.Name, out var renamed) ? new NameReference(renamed) : node;

    protected override Expression RewriteIndex(IndexReference node)
    {
        var index = RewriteExpression(node.Index);
        var hit = _variables.TryGetValue(node.Name, out var renamed);
        if (!hit && ReferenceEquals(index, node.Index))
        {
            return node;
        }

        return new IndexReference(hit ? renamed! : node.Name, index);
    }
}
