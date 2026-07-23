namespace Transpiler.Core.Semantics;

public sealed class Symbol
{
    public Symbol(SymbolKind kind, string name, VariableKind? variableKind = null)
    {
        Kind = kind;
        Name = name;
        VariableKind = variableKind;
    }

    public SymbolKind Kind { get; }

    public string Name { get; }

    /// <summary>Declared data kind for variable symbols; null when undeclared/untyped.</summary>
    public VariableKind? VariableKind { get; }
}
