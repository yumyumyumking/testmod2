namespace Transpiler.Core.Languages;

/// <summary>
/// Routine-kind tags stored on parsed <see cref="Syntax.SubRoutine"/> nodes, mapping each
/// routine back to the plan rule (recipe section kind) that produced it.
/// </summary>
public static class SectionSlots
{
    public const string SubRoutine = "SubRoutine";
    public const string Function = "Function";
    public const string Handler = "Handler";
}
