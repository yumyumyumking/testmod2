namespace Transpiler.Engine.Rules;

/// <summary>Lower-side templates: how the construct is spelled in CL.</summary>
public sealed class LowerSpec
{
    /// <summary>Frame selectors: opening marker template.</summary>
    public string? Begin { get; init; }

    /// <summary>Frame selectors: separator marker template (e.g. the fault handler line).</summary>
    public string? Middle { get; init; }

    /// <summary>Frame selectors: closing marker template.</summary>
    public string? End { get; init; }

    /// <summary>Statement selectors: single-line template.</summary>
    public string? Format { get; init; }
}
