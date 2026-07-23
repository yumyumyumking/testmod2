namespace Transpiler.Core.Syntax;

/// <summary>
/// Variable data kind the engine understands — the closed vocabulary a language's
/// <c>variables.kinds</c> entries bind to. Which kinds a language actually has, how
/// each is spelled in declarations, and which point type carries it in a point area
/// are all declared per language; a kind a target never declares is unrepresentable
/// there (annotations drop, the verifier compares modulo it, allocation refuses).
/// </summary>
public enum VariableKind
{
    Numeric,
    Boolean,
    Byte,
}
