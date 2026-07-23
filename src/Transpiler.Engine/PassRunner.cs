using Microsoft.Extensions.Logging;

namespace Transpiler.Engine;

/// <summary>
/// Folds a program through a pass list with a fresh <see cref="PassContext"/> —
/// shared by the engine's normalization/realization stages and the verifier's
/// read-back (which runs with restricted label folding).
/// </summary>
internal static class PassRunner
{
    public static ProgramSyntax Run(
        ProgramSyntax program,
        IEnumerable<IAstPass> passes,
        LanguageProfile language,
        TranspilerWorkspace workspace,
        DiagnosticBag bag,
        bool foldGeneratedLabelsOnly = false,
        ILogger? log = null)
    {
        // The tier-2 rules are the LANGUAGE'S: normalization runs with the source's
        // mappings (its lifting recognizes its vendor lines), realization with the
        // target's (its lowering spells them).
        var context = new PassContext(language, language, workspace.RulesFor(language), bag, foldGeneratedLabelsOnly);
        foreach (var pass in passes)
        {
            if (log is not null)
            {
                EngineLog.RunningPass(log, pass.Name);
            }

            program = pass.Run(program, context);
        }

        return program;
    }
}
