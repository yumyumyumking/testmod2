namespace Transpiler.Core.Syntax;

/// <summary>
/// Declaration scope the engine understands — the closed vocabulary a language's
/// <c>variables.scopes</c> entries bind to. <see cref="External"/> variables are
/// shared beyond the program (controller-wide); <see cref="Local"/> variables belong
/// to the program. Whether a local may, must, or must not carry a point binding is
/// the scope entry's <c>binding</c> policy, not a separate kind — a bound local is
/// what the LOCALCANREF taxonomy names, an unbound one LOCALLYSCOPED.
/// </summary>
public enum VariableScopeKind
{
    External,
    Local,
}
