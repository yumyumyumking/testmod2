namespace Transpiler.Core.Syntax;

/// <summary>
/// The program's file-header line — the parsed instance of the recipe's namespace
/// section. Generic by design: a header is a <see cref="Name"/> plus whatever
/// <see cref="Fields"/> the language's header pattern captured. The engine attaches
/// no meaning to any field; they are opaque payload that round-trips through the
/// section's emit template. Language-level semantics (e.g. which field names the
/// point area a target allocates into) are declared in the language file
/// (<c>capabilities.allocationField</c>), never hard-coded here.
/// </summary>
public sealed class ProgramHeader : SyntaxNode
{
    public ProgramHeader(string name)
    {
        Name = name;
    }

    public string Name { get; }

    /// <summary>
    /// The header pattern's captures beyond the well-known <c>{name}</c>, keyed by
    /// capture name. Empty for keyword-parsed headers (<c>START Name</c>) and for the
    /// synthesized header; a target's emit template fills any placeholder missing
    /// here from its section's <c>defaults</c>.
    /// </summary>
    public IReadOnlyDictionary<string, string> Fields { get; init; } = NoCaptures;

    /// <summary>Name of the synthesized header (engineers rename it in the output).</summary>
    public const string DefaultName = "Program";

    /// <summary>
    /// A header synthesized when the source language has no file-header section
    /// (e.g. MATLAB) yet the target requires one. It carries no fields — the target's
    /// section <c>defaults</c> supply whatever its template needs — and the
    /// round-trip verifier compares modulo this header.
    /// </summary>
    public static ProgramHeader Synthetic { get; } = new(DefaultName);
}
