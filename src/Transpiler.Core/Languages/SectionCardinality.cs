namespace Transpiler.Core.Languages;

/// <summary>How often a <see cref="SectionRule"/> may appear at its level of the program.</summary>
public enum SectionCardinality
{
    /// <summary>Any number of appearances, including none.</summary>
    ZeroOrMany,

    /// <summary>
    /// At least one appearance. A program with none gets a diagnostic (the parse
    /// itself still recovers — cardinality is checked after the walk, not enforced
    /// by failing it).
    /// </summary>
    OneOrMany,
}
