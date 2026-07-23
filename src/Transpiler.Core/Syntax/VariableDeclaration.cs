namespace Transpiler.Core.Syntax;

public sealed class VariableDeclaration : SyntaxNode
{
    public VariableDeclaration(
        VariableScopeKind scope,
        string name,
        VariableKind? kind = null,
        PointBinding? binding = null,
        bool isGenerated = false)
    {
        Scope = scope;
        Name = name;
        Kind = kind;
        Binding = binding;
        IsGenerated = isGenerated;
    }

    public VariableScopeKind Scope { get; }

    public string Name { get; }

    /// <summary>Declared data kind; null when omitted (defaults to Numeric).</summary>
    public VariableKind? Kind { get; }

    /// <summary>Point binding; null when unbound (or auto-allocated later).</summary>
    public PointBinding? Binding { get; }

    /// <summary>True for declarations injected by lowering (e.g. REPEAT counters).</summary>
    public bool IsGenerated { get; }

    public VariableKind EffectiveKind => Kind ?? VariableKind.Numeric;
}
