namespace Transpiler.Engine.Transform;

/// <summary>
/// Shared state threaded through a pass pipeline run. Pipelines are language-neutral
/// (SPEC Appendix B): normalization runs with source==target==the language being read,
/// realization with source==target==the language being written; passes consult the
/// languages, never a direction.
/// </summary>
public sealed class PassContext
{
    public PassContext(
        LanguageProfile sourceLanguage,
        LanguageProfile targetLanguage,
        MappingRuleSet rules,
        DiagnosticBag diagnostics,
        bool foldGeneratedLabelsOnly = false)
    {
        SourceLanguage = sourceLanguage;
        TargetLanguage = targetLanguage;
        Rules = rules;
        Diagnostics = diagnostics;
        FoldGeneratedLabelsOnly = foldGeneratedLabelsOnly;
        Labels = new LabelAllocator(targetLanguage.Labels.GeneratedPrefix);
    }

    public LanguageProfile SourceLanguage { get; }

    public LanguageProfile TargetLanguage { get; }

    public MappingRuleSet Rules { get; }

    public DiagnosticBag Diagnostics { get; }

    public LabelAllocator Labels { get; }

    /// <summary>
    /// When set, the structurer folds only labels carrying the generated prefix —
    /// i.e. idioms this transpiler's own lowering emitted. The verifier's read-back
    /// runs in this mode so a natively-written GOTO idiom in the source (which the
    /// lowerer reproduced verbatim) is not re-structured into a shape the source
    /// never had, which would be a false round-trip divergence. User-facing lifting
    /// keeps full shape-based folding.
    /// </summary>
    public bool FoldGeneratedLabelsOnly { get; }

    /// <summary>Identifier comparison for this run, from the source language.</summary>
    public StringComparison NameComparison => SourceLanguage.NameComparison;

    // NOTE deliberately no pass-private state here: accumulators that one pass both
    // produces and consumes (REPEAT counter declarations, folded-counter names) are
    // local to their pass — passes are constructed fresh per pipeline by
    // PipelineFactory. This context carries only genuinely cross-cutting run state.
}
