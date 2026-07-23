namespace Transpiler.Core.Syntax;

/// <summary>
/// A vendor line owned by a tier-2 mapping rule: either produced by lowering
/// (rendered text) or recognized by the CL parser via the rule's lift pattern.
/// </summary>
public sealed class MarkerStatement : Statement
{
    public MarkerStatement(string ruleName, string mappingId, MarkerRole role, IReadOnlyDictionary<string, string> captures, string text)
    {
        RuleName = ruleName;
        MappingId = mappingId;
        Role = role;
        Captures = captures;
        Text = text;
    }

    public string RuleName { get; }

    public string MappingId { get; }

    public MarkerRole Role { get; }

    public IReadOnlyDictionary<string, string> Captures { get; }

    /// <summary>The rendered/raw line as it appears in CL.</summary>
    public string Text { get; }
}
