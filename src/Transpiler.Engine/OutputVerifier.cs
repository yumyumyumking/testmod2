using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Transpiler.Engine;

/// <summary>
/// Stage 6 of every run — extracted from <see cref="TranspileEngine"/> so pipeline
/// orchestration and the verification algorithm read separately.
/// 6a: the emitted text must re-parse AND re-bind under the target language; failures
/// wrap into CLX3001 and nothing is ever written from a failing run.
/// 6b (when <see cref="TranspileOptions.VerifyRoundTrip"/>): the output is read back
/// to the IR with the target's normalization passes in restricted fold mode — only
/// transpiler-generated labels re-fold, so natively-written GOTO idioms compare
/// shape-for-shape — and the dump is compared structurally with the source IR
/// (CLX3002 on divergence). A target without a file-header section is compared
/// header-stripped: the drop was already reported at emit as CLX3004.
/// </summary>
public sealed class OutputVerifier
{
    private readonly ILogger _log;

    public OutputVerifier(ILogger? log = null)
    {
        _log = log ?? NullLogger.Instance;
    }

    public void Verify(
        string output,
        ProgramSyntax sourceIr,
        LanguageProfile targetLanguage,
        TranspilerWorkspace workspace,
        TranspileOptions options,
        DiagnosticBag diagnostics)
    {
        // 6a. Emitted output must re-parse and re-bind under the target language.
        var outputText = SourceText.From(output);
        var outputTree = SyntaxTree.Parse(outputText, targetLanguage, workspace.PatternsFor(targetLanguage));

        var verifyErrors = outputTree.Diagnostics
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if (verifyErrors.Count == 0)
        {
            var outputModel = Binder.Bind(outputTree, targetLanguage);
            verifyErrors = outputModel.Diagnostics
                .Where(static d => d.Severity == DiagnosticSeverity.Error)
                .ToList();
        }

        if (verifyErrors.Count > 0)
        {
            var summary = string.Join(" | ", verifyErrors.Take(3).Select(static d => d.ToString()));
            diagnostics.Report(DiagnosticCodes.OutputFailedToParse, null, summary);
            return;
        }

        // 6b. Round trip: read the output back to the IR and compare with the source IR.
        if (options.VerifyRoundTrip)
        {
            var readbackBag = new DiagnosticBag(outputText);
            var readbackIr = PassRunner.Run(
                outputTree.Root,
                PipelineFactory.NormalizationPasses(targetLanguage),
                targetLanguage,
                workspace,
                readbackBag,
                foldGeneratedLabelsOnly: true);

            // Compare modulo what the target cannot represent: its file header (the source
            // header is dropped when the target has no namespace section; a synthesized
            // header is dropped when the source had none), header fields and section
            // captures the target's emit templates carry no placeholder for, and
            // declaration kinds the target's variable model never declares (all of
            // them, for an untyped target like MATLAB).
            var targetPlan = targetLanguage.Plan;
            var targetVariables = targetLanguage.Variables;
            var comparableSource = Canonicalize(
                sourceIr, stripHeader: targetPlan.Namespace is null, targetVariables, targetPlan);
            var comparableReadback = Canonicalize(
                readbackIr, stripHeader: sourceIr.Header is null && readbackIr.Header is not null, targetVariables, targetPlan);

            var expected = TreeDumper.Dump(comparableSource);
            var actual = TreeDumper.Dump(comparableReadback);
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                diagnostics.Report(DiagnosticCodes.RoundTripDivergence, null, "program");
                _log.LogDebug("Round-trip expected: {Expected}", expected);
                _log.LogDebug("Round-trip actual:   {Actual}", actual);
            }
        }
    }

    /// <summary>
    /// Rebuilds a program for structural comparison, dropping what the target language
    /// cannot represent: the file header (<paramref name="stripHeader"/>), header
    /// fields and section extras without a placeholder in the target's emit templates,
    /// and declaration kinds the target's variable model never declares — erased on
    /// both sides, so representable content still has to match exactly.
    /// </summary>
    private static ProgramSyntax Canonicalize(
        ProgramSyntax program, bool stripHeader, VariablePlan targetVariables, SectionPlan targetPlan)
    {
        var header = stripHeader ? null : CanonicalHeader(program.Header, targetPlan.Namespace);

        IReadOnlyList<VariableDeclaration> declarations = program.Declarations
            .Select(d => new VariableDeclaration(
                d.Scope,
                d.Name,
                d.Kind is { } kind && targetVariables.Supports(kind) ? kind : null,
                d.Binding,
                d.IsGenerated))
            .ToList();

        var mainRule = targetPlan.FindRule(SectionContent.MainRoutine);
        var subRule = targetPlan.FindRule(SectionContent.SubRoutine);
        var mainRoutines = program.MainRoutines
            .Select(mainRoutine => new MainRoutine(mainRoutine.Name, mainRoutine.SubRoutines.Select(subRoutine => CanonicalRoutine(subRoutine, subRule)).ToList())
            {
                ExtraCaptures = RepresentableExtras(mainRoutine.ExtraCaptures, mainRule),
            })
            .ToList();

        var fileRoutines = program.FileRoutines
            .Select(routine => CanonicalRoutine(routine, RuleForKind(targetPlan, routine.Kind)))
            .ToList();

        return new ProgramSyntax(header, declarations, mainRoutines, fileRoutines);
    }

    private static ProgramHeader? CanonicalHeader(ProgramHeader? header, SectionRule? targetNamespace)
    {
        if (header is null || targetNamespace is null)
        {
            return header;
        }

        return new ProgramHeader(header.Name) { Fields = RepresentableExtras(header.Fields, targetNamespace) };
    }

    private static SubRoutine CanonicalRoutine(SubRoutine subRoutine, SectionRule? rule) =>
        new(subRoutine.Name, subRoutine.Body, subRoutine.Kind) { ExtraCaptures = RepresentableExtras(subRoutine.ExtraCaptures, rule) };

    /// <summary>
    /// The canonical field set of one section instance for structural comparison:
    /// the target template's placeholders, each taken from the node's own captures
    /// or — exactly as emission does — from the section's <c>defaults</c>. Applied
    /// to both comparison sides, so a field only the read-back carries (because the
    /// emitter filled it from a default) compares equal instead of diverging. A
    /// keyword-mode rule (no template) represents only the name.
    /// </summary>
    private static IReadOnlyDictionary<string, string> RepresentableExtras(
        IReadOnlyDictionary<string, string> extras, SectionRule? rule)
    {
        if (rule?.EmitTemplate is not { } template)
        {
            return SyntaxNode.NoCaptures;
        }

        var canonical = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var placeholder in SectionPatterns.Placeholders(template))
        {
            if (string.Equals(placeholder, SectionPatterns.NameCapture, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (extras.TryGetValue(placeholder, out var value) || rule.Defaults.TryGetValue(placeholder, out value))
            {
                canonical[placeholder] = value;
            }
        }

        return canonical.Count == 0 ? SyntaxNode.NoCaptures : canonical;
    }

    private static SectionRule? RuleForKind(SectionPlan plan, string kind) => kind switch
    {
        SectionSlots.Function => plan.FindRule(SectionContent.Function),
        SectionSlots.Handler => plan.FindRule(SectionContent.Handler),
        _ => plan.FindRule(SectionContent.SubRoutine),
    };
}
