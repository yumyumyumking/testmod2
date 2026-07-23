namespace Transpiler.Core.Languages;

/// <summary>
/// Keyword spellings for one language. Multi-word phrases are allowed; matching is
/// whitespace-insensitive between words and honours <see cref="LanguageProfile.CaseSensitive"/>.
/// All values here are placeholders until the real controller keywords are confirmed
/// (SPEC §13) — correcting them is a JSON edit, never a code change.
/// </summary>
public sealed class KeywordTable
{
    // Section spellings (SEQUENCE/PHASE/STEP/END…) are NOT keywords: they live in the
    // 'recipe' block, per section, as start/end shorthand or format patterns — a
    // shaped header line (fields, punctuation) is entirely the recipe's pattern.
    // Declaration spellings (EXTERNAL/LOCAL, NUMBER/LOGICAL, NN/FL) are not keywords
    // either: they live in the 'variables' block, per scope and per kind.

    public string If { get; init; } = "IF";
    public string Then { get; init; } = "THEN";
    public string Else { get; init; } = "ELSE";
    public string ElseIf { get; init; } = "ELSIF";
    public string EndIf { get; init; } = "ENDIF";
    public string While { get; init; } = "WHILE";
    public string Do { get; init; } = "DO";
    public string EndWhile { get; init; } = "ENDWHILE";
    public string Repeat { get; init; } = "REPEAT";
    public string Times { get; init; } = "TIMES";
    public string EndRepeat { get; init; } = "ENDREPEAT";

    // Counting FOR loop (structured languages only; enabled by capabilities.forLoops).
    // The header uses range syntax "var = from : to" or "var = from : step : to".
    public string For { get; init; } = "FOR";
    public string EndFor { get; init; } = "ENDFOR";
    public string Try { get; init; } = "TRY";
    public string Catch { get; init; } = "CATCH";
    public string EndTry { get; init; } = "ENDTRY";
    public string Array { get; init; } = "ARRAY";
    public string Set { get; init; } = "SET";
    public string Reset { get; init; } = "RESET";
    public string Goto { get; init; } = "GOTO";
    public string Call { get; init; } = "CALL";
    public string Return { get; init; } = "RETURN";
    public string BoolTrue { get; init; } = "ON";
    public string BoolFalse { get; init; } = "OFF";
    public string And { get; init; } = "AND";
    public string Or { get; init; } = "OR";
    public string Not { get; init; } = "NOT";

    // Relational operator spellings used by the emitter. Parsing accepts these plus the
    // punctuation tokens (=, ==, <>, ~=), so only output rendering depends on them.
    public string Equal { get; init; } = "=";
    public string NotEqual { get; init; } = "<>";
}
