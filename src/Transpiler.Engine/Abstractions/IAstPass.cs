namespace Transpiler.Engine.Abstractions;

/// <summary>
/// Tier-3 extension point (SPEC §7.3): a tree-to-tree transformation. The built-in
/// lowering and lifting passes implement this same interface; user plugins are
/// appended through <see cref="TranspileOptions.ExtraPasses"/>. It lives in the
/// Engine (not Core.Abstractions) because its <see cref="PassContext"/> parameter is
/// Engine run-state — a Core abstraction referencing it would invert the dependency.
/// </summary>
public interface IAstPass
{
    string Name { get; }

    ProgramSyntax Run(ProgramSyntax program, PassContext context);
}
