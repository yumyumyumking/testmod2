namespace Transpiler.Core.Syntax;

public sealed class ProgramSyntax : SyntaxNode
{
    public ProgramSyntax(
        ProgramHeader? header,
        IReadOnlyList<VariableDeclaration> declarations,
        IReadOnlyList<MainRoutine> mainRoutines,
        IReadOnlyList<SubRoutine>? fileRoutines = null)
    {
        Header = header;
        Declarations = declarations;
        MainRoutines = mainRoutines;
        FileRoutines = fileRoutines ?? Array.Empty<SubRoutine>();
    }

    /// <summary>
    /// Null when the file-header section is missing (an error in languages whose
    /// recipe requires one) or when the language has no namespace section at all.
    /// </summary>
    public ProgramHeader? Header { get; }

    public IReadOnlyList<VariableDeclaration> Declarations { get; }

    public IReadOnlyList<MainRoutine> MainRoutines { get; }

    /// <summary>
    /// File-level Function/Handler routines, in source order. Empty for languages
    /// without those sections (CL and CLX both).
    /// </summary>
    public IReadOnlyList<SubRoutine> FileRoutines { get; }
}
