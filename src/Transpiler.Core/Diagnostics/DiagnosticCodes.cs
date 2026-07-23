namespace Transpiler.Core.Diagnostics;

/// <summary>
/// The diagnostic catalogue (SPEC §10). Codes are stable public API; message
/// templates use composite formatting.
/// </summary>
public static class DiagnosticCodes
{
    // Lexical / syntactic (CLX0xxx)
    public static readonly DiagnosticCode UnrecognizedCharacter =
        new("CLX0001", DiagnosticSeverity.Error, "Unrecognized character '{0}'.");

    public static readonly DiagnosticCode UnexpectedToken =
        new("CLX0102", DiagnosticSeverity.Error, "Unexpected {0}; expected {1}. Statement skipped.");

    public static readonly DiagnosticCode UnknownStatement =
        new("CLX0103", DiagnosticSeverity.Error, "Unrecognized statement '{0}'.");

    public static readonly DiagnosticCode SectionNotClosed =
        new("CLX0110", DiagnosticSeverity.Error, "'{0}' section is not closed with '{1}'.");

    public static readonly DiagnosticCode MissingMainRoutine =
        new("CLX0111", DiagnosticSeverity.Error, "No '{0}' blocks found after the declaration header.");

    public static readonly DiagnosticCode MissingHeader =
        new("CLX0112", DiagnosticSeverity.Error, "Missing mandatory header line: {0}.");

    public static readonly DiagnosticCode EndNameMismatch =
        new("CLX0113", DiagnosticSeverity.Warning, "'{0} {1}' does not match the section name '{2}'.");

    public static readonly DiagnosticCode StatementOutsideSubRoutine =
        new("CLX0114", DiagnosticSeverity.Error, "Statements must appear inside a {0} block.");

    public static readonly DiagnosticCode RequiredSectionMissing =
        new("CLX0115", DiagnosticSeverity.Error, "Required '{0}' section is missing from {1}.");

    public static readonly DiagnosticCode SectionNotAllowedHere =
        new("CLX0116", DiagnosticSeverity.Error, "A '{0}' section is not allowed {1}.");

    public static readonly DiagnosticCode SectionHeaderMismatch =
        new("CLX0117", DiagnosticSeverity.Error, "'{0}' line does not match the expected form: {1}.");

    public static readonly DiagnosticCode UnterminatedString =
        new("CLX0120", DiagnosticSeverity.Error, "Unterminated string literal.");

    public static readonly DiagnosticCode BindingNotAllowed =
        new("CLX0121", DiagnosticSeverity.Error, "A '{0}' declaration cannot carry a point binding in this language.");

    public static readonly DiagnosticCode BindingRequired =
        new("CLX0122", DiagnosticSeverity.Error, "A '{0}' declaration requires a point binding in this language.");

    // Semantic (CLX1xxx)
    public static readonly DiagnosticCode UndeclaredVariable =
        new("CLX1001", DiagnosticSeverity.Error, "Use of undeclared variable '{0}'.");

    public static readonly DiagnosticCode DuplicateDeclaration =
        new("CLX1002", DiagnosticSeverity.Error, "Duplicate declaration of '{0}'.");

    public static readonly DiagnosticCode GotoTargetNotFound =
        new("CLX1003", DiagnosticSeverity.Error, "GOTO target '{0}' is not defined in this routine.");

    // Warning, not error: CALL targets may legitimately live in another controller
    // file, which single-file analysis cannot see.
    public static readonly DiagnosticCode CallTargetNotFound =
        new("CLX1004", DiagnosticSeverity.Warning, "CALL to unknown routine '{0}' (not defined in this file).");

    public static readonly DiagnosticCode SubroutineNeverCalled =
        new("CLX1005", DiagnosticSeverity.Warning, "Subroutine '{0}' is never called.");

    public static readonly DiagnosticCode LabelNeverTargeted =
        new("CLX1006", DiagnosticSeverity.Warning, "Label '{0}' is never targeted by a GOTO.");

    public static readonly DiagnosticCode ReturnInMain =
        new("CLX1007", DiagnosticSeverity.Error, "RETURN is not allowed in the main routine for this language.");

    public static readonly DiagnosticCode RecursiveCall =
        new("CLX1008", DiagnosticSeverity.Warning, "Recursive CALL cycle detected: {0}.");

    public static readonly DiagnosticCode DuplicateLabel =
        new("CLX1009", DiagnosticSeverity.Error, "Duplicate label '{0}' in this routine.");

    public static readonly DiagnosticCode IndexingMismatch =
        new("CLX1101", DiagnosticSeverity.Error, "'{0}' is {1}, but is used {2}.");

    public static readonly DiagnosticCode UnstructuredJump =
        new("CLX1201", DiagnosticSeverity.Info, "Unstructured jump (GOTO '{0}') in CLX source.");

    // Transform (CLX2xxx)
    public static readonly DiagnosticCode UnstructuredFlowRetained =
        new("CLX2101", DiagnosticSeverity.Info, "Lift: unstructured control flow retained in routine '{0}'.");

    public static readonly DiagnosticCode GeneratedPrefixCollision =
        new("CLX2102", DiagnosticSeverity.Error, "Name '{0}' collides with the generated-label prefix '{1}'.");

    public static readonly DiagnosticCode ExpressionNotRepresentable =
        new("CLX2301", DiagnosticSeverity.Error, "Expression is not representable in the target language (complex conditions disabled).");

    public static readonly DiagnosticCode CapabilityMissing =
        new("CLX2302", DiagnosticSeverity.Error, "Construct '{0}' requires capability or mapping rule '{1}', which is not available.");

    public static readonly DiagnosticCode IndexedAccessUnsupported =
        new("CLX2303", DiagnosticSeverity.Error, "Indexed access is only supported as a whole-statement load or store.");

    public static readonly DiagnosticCode UnloweredConstruct =
        new("CLX2304", DiagnosticSeverity.Error, "Internal: construct '{0}' survived lowering and cannot be emitted as CL.");

    public static readonly DiagnosticCode PointCapacityExceeded =
        new("CLX2401", DiagnosticSeverity.Error, "Point area '{0}' is out of {1} points: {2} local(s) requested, capacity is {3}.");

    public static readonly DiagnosticCode DuplicatePointBinding =
        new("CLX2402", DiagnosticSeverity.Warning, "Point {0}.{1}({2}) is bound by more than one LOCAL declaration.");

    // Emit / verify (CLX3xxx)
    public static readonly DiagnosticCode OutputFailedToParse =
        new("CLX3001", DiagnosticSeverity.Error, "Emitted output failed verification: {0}");

    public static readonly DiagnosticCode RoundTripDivergence =
        new("CLX3002", DiagnosticSeverity.Error, "Round-trip verification divergence in routine '{0}'.");

    public static readonly DiagnosticCode SectionNotRepresentable =
        new("CLX3003", DiagnosticSeverity.Error, "Target language '{0}' has no {1} section; routine '{2}' cannot be represented.");

    public static readonly DiagnosticCode HeaderNotRepresentable =
        new("CLX3004", DiagnosticSeverity.Info, "Target language '{0}' has no file-header section; the source header was omitted.");

    // Configuration (CLX4xxx). CLX4002 (rule-pack schema v1) is retired — tier-2
    // mappings live inside each language file; there are no separate rule packs.
    public static readonly DiagnosticCode ConfigInvalid =
        new("CLX4001", DiagnosticSeverity.Error, "Language file invalid: {0}");

    public static readonly DiagnosticCode UnknownLanguage =
        new("CLX4003", DiagnosticSeverity.Error, "Unknown language '{0}'. Loaded languages: {1}.");
}
