namespace Transpiler.Engine.Transform;

/// <summary>Shared program-shape traversal for passes that transform sub-routine bodies.</summary>
internal static class ProgramExtensions
{
    public static ProgramSyntax MapSubRoutines(this ProgramSyntax program, Func<SubRoutine, SubRoutine> map)
    {
        var mainRoutines = program.MainRoutines
            .Select(mainRoutine => mainRoutine.WithSubRoutines(mainRoutine.SubRoutines.Select(map).ToList()))
            .ToList();

        // File-level Function/Handler routines carry statements too and run through
        // the same passes — a lowering that skipped them would emit structured code.
        var fileRoutines = program.FileRoutines.Select(map).ToList();

        return new ProgramSyntax(program.Header, program.Declarations, mainRoutines, fileRoutines)
        {
            Span = program.Span,
            LeadingComments = program.LeadingComments,
        };
    }

    public static ProgramSyntax WithDeclarations(this ProgramSyntax program, IReadOnlyList<VariableDeclaration> declarations) =>
        new(program.Header, declarations, program.MainRoutines, program.FileRoutines)
        {
            Span = program.Span,
            LeadingComments = program.LeadingComments,
        };
}
