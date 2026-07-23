namespace Transpiler.Core.Languages;

/// <summary>What one <see cref="SectionRule"/> of a <see cref="SectionPlan"/> contains.</summary>
public enum SectionContent
{
    /// <summary>
    /// The outermost container and file header (SEQUENCE / MODULE). At most one per
    /// language; when present it is the file prologue, and with a terminator it wraps
    /// the whole program namespace-style.
    /// </summary>
    Namespace,

    /// <summary>
    /// EXTERNAL/LOCAL variable declarations. Wherever they appear, they hoist into
    /// the program's single file-scope declaration list (emission is canonical: one
    /// block under the header).
    /// </summary>
    Declarations,

    /// <summary>A main routine (PHASE): a named container of sub-routines.</summary>
    MainRoutine,

    /// <summary>
    /// A sub-routine (STEP). Normally hosted inside a main routine; hosted at file
    /// level when the language has no main-routine section (content shifts up a level).
    /// </summary>
    SubRoutine,

    /// <summary>A file-level FUNCTION routine.</summary>
    Function,

    /// <summary>A file-level HANDLER routine.</summary>
    Handler,

    /// <summary>
    /// Bare statements — the catch-all for languages whose sections are all absent at
    /// this level, so the body itself is the content (wrapped in an implicit routine).
    /// Always the last alternative of its level: it matches anything.
    /// </summary>
    Statements,
}
