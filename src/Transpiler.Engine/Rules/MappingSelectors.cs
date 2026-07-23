namespace Transpiler.Engine.Rules;

/// <summary>
/// The AST shapes a tier-2 mapping rule may bind to (SPEC §7.2). Adding a new
/// selector is a tier-3 (code) change; remapping a selector's spelling is JSON.
/// </summary>
public static class MappingSelectors
{
    /// <summary>TRY/CATCH frame: begin/middle/end markers around body and handler.</summary>
    public const string TryBlock = "TryBlock";

    /// <summary>ARRAY name[size] declaration line.</summary>
    public const string ArrayDeclaration = "ArrayDeclaration";

    /// <summary>Whole-statement store: array[index] = value.</summary>
    public const string IndexedStore = "IndexedStore";

    /// <summary>Whole-statement load: dest = array[index].</summary>
    public const string IndexedLoad = "IndexedLoad";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        TryBlock, ArrayDeclaration, IndexedStore, IndexedLoad,
    };

    public static bool IsFrame(string selector) => selector == TryBlock;
}
