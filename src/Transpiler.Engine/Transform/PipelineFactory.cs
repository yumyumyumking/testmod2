namespace Transpiler.Engine.Transform;

/// <summary>
/// The retargetable core (SPEC Appendix B). The structured AST is the language-neutral
/// intermediate representation (IR): structured control flow (IF/WHILE/REPEAT/TRY),
/// unbound locals, no vendor marker lines. Every language contributes two pipelines
/// derived from its language capabilities:
///
///   front end:  parse → NORMALIZATION passes  (language shape → IR)
///   back end:   REALIZATION passes → emit     (IR → language shape)
///
/// Transpiling any of M languages to any of N is then front(M) + back(N) — M+N
/// components instead of M×N pairwise translators. A structured language (blockIf)
/// needs no passes at all; a flat language raises GOTO idioms and marker frames on
/// the way in and lowers them on the way out.
/// </summary>
public static class PipelineFactory
{
    /// <summary>Passes that raise a freshly parsed tree to the IR.</summary>
    public static IReadOnlyList<IAstPass> NormalizationPasses(LanguageProfile source)
    {
        var passes = new List<IAstPass>();

        if (!source.Capabilities.BlockIf)
        {
            // Flat language: reassemble vendor frames, then structure GOTO idioms.
            passes.Add(new MappingLiftPass());
            passes.Add(new StructurerPass());
        }

        return passes;
    }

    /// <summary>Passes that realize the IR into the target language's shape.</summary>
    public static IReadOnlyList<IAstPass> RealizationPasses(LanguageProfile target)
    {
        var passes = new List<IAstPass>();

        if (!target.Capabilities.BlockIf)
        {
            // Flat language: vendor frames first (bodies stay structured for the
            // structural pass), then structured control flow onto GOTO/labels.
            passes.Add(new MappingLowerPass());
            passes.Add(new StructuralLoweringPass());
        }

        if (target.Capabilities.PointAllocation)
        {
            passes.Add(new PointAllocationPass());
        }

        return passes;
    }

    /// <summary>
    /// Whether parsing this language needs the mapping rules' vendor patterns
    /// (flat languages carry marker lines; structured ones do not).
    /// </summary>
    public static bool NeedsVendorPatterns(LanguageProfile language) => !language.Capabilities.BlockIf;
}
