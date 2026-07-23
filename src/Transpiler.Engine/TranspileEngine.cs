using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Transpiler.Engine;

/// <summary>
/// The retargetable pipeline (SPEC §6 + Appendix B): parse → bind → normalize to the
/// language-neutral IR → realize into the target language → emit → verify. Any source
/// language and any target language compose through the IR (M+N, not M×N): the pass
/// lists come from <see cref="PipelineFactory"/>, derived from language capabilities.
/// Verification re-parses and re-binds the emitted text, then reads it back to the IR
/// and compares structurally with the source IR (round-trip guarantee, any pair).
/// </summary>
public sealed class TranspileEngine
{
    private readonly ILogger<TranspileEngine> _log;
    private readonly OutputVerifier _verifier;

    public TranspileEngine(ILogger<TranspileEngine>? logger = null)
    {
        _log = logger ?? NullLogger<TranspileEngine>.Instance;
        _verifier = new OutputVerifier(_log);
    }

    /// <summary>
    /// Transpiles between any two registered languages (by language name) through the
    /// IR. Unknown names are reported as CLX4003 and the run fails gracefully — a
    /// dropped or misspelled language never crashes the pipeline.
    /// </summary>
    public TranspileResult Transpile(
        string sourceText,
        string from,
        string to,
        TranspilerWorkspace workspace,
        TranspileOptions? options = null,
        string fileName = "<memory>")
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        ArgumentNullException.ThrowIfNull(workspace);
        options ??= TranspileOptions.Default;

        var stopwatch = Stopwatch.StartNew();
        var text = SourceText.From(sourceText);

        // One diagnostic sink for the whole compilation: every stage reports into it and
        // the result reads from it — no per-stage bags to create and merge. (The verifier's
        // internal re-parses of the emitted text keep their own bags, since those problems
        // are about the output, not this source.)
        var diagnostics = new DiagnosticBag(text);

        var known = string.Join(", ", workspace.LanguageNames);
        if (!workspace.TryGetLanguage(from ?? string.Empty, out var sourceLanguage))
        {
            diagnostics.Report(DiagnosticCodes.UnknownLanguage, null, from ?? "<null>", known);
        }

        if (!workspace.TryGetLanguage(to ?? string.Empty, out var targetLanguage))
        {
            diagnostics.Report(DiagnosticCodes.UnknownLanguage, null, to ?? "<null>", known);
        }

        if (diagnostics.HasErrors)
        {
            _log.LogWarning("Transpile aborted: unknown language ({From} -> {To}); loaded: {Known}.", from, to, known);
            return Finish(string.Empty, diagnostics, stopwatch);
        }

        EngineLog.Transpiling(_log, from, to, fileName);

        // 1. Front end: parse.
        var tree = SyntaxTree.Parse(text, sourceLanguage, workspace.PatternsFor(sourceLanguage), diagnostics);
        if (diagnostics.HasErrors)
        {
            return Finish(string.Empty, diagnostics, stopwatch);
        }

        // 2. Front end: bind.
        Binder.Bind(tree, sourceLanguage, diagnostics);
        if (diagnostics.HasErrors)
        {
            return Finish(string.Empty, diagnostics, stopwatch);
        }

        // 3. Front end: normalize to the IR.
        var ir = PassRunner.Run(tree.Root, PipelineFactory.NormalizationPasses(sourceLanguage), sourceLanguage, workspace, diagnostics, log: _log);
        if (diagnostics.HasErrors)
        {
            return Finish(string.Empty, diagnostics, stopwatch);
        }

        // 4. Back end: realize the IR into the target language (+ plugin passes).
        var realized = PassRunner.Run(ir, PipelineFactory.RealizationPasses(targetLanguage).Concat(options.ExtraPasses), targetLanguage, workspace, diagnostics, log: _log);
        if (diagnostics.HasErrors)
        {
            return Finish(string.Empty, diagnostics, stopwatch);
        }

        // 5. Back end: emit.
        var output = Emitter.Emit(realized, targetLanguage, options.Formatting, diagnostics);
        if (diagnostics.HasErrors)
        {
            return Finish(string.Empty, diagnostics, stopwatch);
        }

        // 6. Verify.
        _verifier.Verify(output, ir, targetLanguage, workspace, options, diagnostics);

        EngineLog.TranspileCompleted(
            _log,
            stopwatch.ElapsedMilliseconds,
            diagnostics.Count(static d => d.Severity == DiagnosticSeverity.Warning),
            diagnostics.Count(static d => d.Severity == DiagnosticSeverity.Error));

        return Finish(output, diagnostics, stopwatch);
    }

    private static TranspileResult Finish(string output, DiagnosticBag diagnostics, Stopwatch stopwatch)
    {
        stopwatch.Stop();
        return new TranspileResult(output, diagnostics.ToList(), stopwatch.Elapsed);
    }
}
