namespace Transpiler.Core.Languages;

/// <summary>What the target language is capable of expressing (SPEC §7.1).</summary>
public sealed class LanguageCapabilities
{
    /// <summary>True when IF may carry statement blocks (CLX); false for single-action IF (CL).</summary>
    public bool BlockIf { get; init; }

    /// <summary>Parenthesized boolean expressions allowed in conditions.</summary>
    public bool ComplexConditions { get; init; } = true;

    /// <summary>Arithmetic in assignments allowed (needed by REPEAT lowering).</summary>
    public bool Arithmetic { get; init; } = true;

    /// <summary>"endScan" (RETURN ends the scan early) or "forbidden".</summary>
    public string ReturnInMain { get; init; } = "endScan";

    /// <summary>Maximum LOCAL variables per point type (NN/FL) in one database.</summary>
    public int LocalCapacity { get; init; } = 1000;

    /// <summary>
    /// True when this language's local variables must be bound to points in a named
    /// point area; the back end then runs the allocation pass for unbound locals.
    /// </summary>
    public bool PointAllocation { get; init; }

    /// <summary>
    /// Name of the header field (a capture of the namespace section's format) that
    /// carries the point area allocations go into. Required when
    /// <see cref="PointAllocation"/> is true; the namespace section's
    /// <c>defaults</c> must supply the field, so headerless sources still allocate
    /// consistently with the header the emitter synthesizes.
    /// </summary>
    public string? AllocationField { get; init; }

    // ---- Grammar-shape options (concrete syntax only; the AST is unaffected) --------
    // These let one recursive-descent parser/emitter cover languages whose block shape
    // differs from the CL family (e.g. MATLAB: no THEN/DO, universal 'end', bare catch).

    /// <summary>
    /// True when a block IF writes THEN after the condition (CL/CLX); false for
    /// languages that omit it (MATLAB: <c>if cond</c>). Emission drops THEN when false.
    /// </summary>
    public bool RequiresThen { get; init; } = true;

    /// <summary>
    /// True when a WHILE writes DO after the condition (CL/CLX); false for languages
    /// that omit it (MATLAB: <c>while cond</c>). Emission drops DO when false.
    /// </summary>
    public bool RequiresDo { get; init; } = true;

    /// <summary>
    /// True when CATCH names its fault variable in parentheses — <c>CATCH (e)</c>
    /// (CL/CLX); false for a bare, space-separated form — <c>catch e</c> (MATLAB).
    /// </summary>
    public bool CatchWithParens { get; init; } = true;

    /// <summary>
    /// True when flag writes use SET/RESET statements (CL/CLX); false when they are
    /// plain boolean assignments — <c>alarm = true</c> (MATLAB). Emission only:
    /// a <c>SetStatement</c> renders as an assignment to the boolTrue/boolFalse literal.
    /// </summary>
    public bool SetReset { get; init; } = true;

    /// <summary>
    /// True when the language offers a counting FOR loop with range syntax
    /// (<c>for v = a:b</c>). The front end desugars it to the equivalent WHILE plus an
    /// implicit local counter, so no new IR node or transform pass is involved.
    /// </summary>
    public bool ForLoops { get; init; }

    /// <summary>
    /// True when a routine is invoked by juxtaposition — <c>cooldown()</c> — rather than
    /// with a CALL keyword. Statement-position <c>name(args)</c> parses to a CALL (args
    /// are discarded, as CL has no argument passing) and a CALL emits as <c>name()</c>.
    /// </summary>
    public bool ImplicitCall { get; init; }

}
