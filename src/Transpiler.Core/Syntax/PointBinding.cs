namespace Transpiler.Core.Syntax;

/// <summary>
/// A point binding: <c>!Area.NN(index)</c> / <c>!Area.FL(index)</c>. A control
/// language's local variables live in a named point area (memory bank); languages
/// that omit bindings get slots allocated deterministically when lowering into a
/// language that requires them (<c>capabilities.pointAllocation</c>).
/// </summary>
public sealed class PointBinding
{
    public PointBinding(string area, string pointType, string index)
    {
        Area = area;
        PointType = pointType;
        Index = index;
    }

    /// <summary>The point area (memory bank) the point lives in.</summary>
    public string Area { get; }

    /// <summary>Point kind as spelled in the language (default NN or FL).</summary>
    public string PointType { get; }

    /// <summary>Slot index literal.</summary>
    public string Index { get; }
}
