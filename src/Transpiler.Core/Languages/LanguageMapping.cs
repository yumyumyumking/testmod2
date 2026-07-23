namespace Transpiler.Core.Languages;

/// <summary>
/// One vendor-construct correspondence declared inside a language file (the
/// <c>mappings</c> block): how an IR construct this language cannot express natively
/// is spelled here. Paired by construction — every mapping carries its
/// <see cref="Lowering"/> (how the construct is written into this language) and its
/// <see cref="Lifting"/> (how those lines are recognized and read back) side by side,
/// with one <see cref="Selector"/> naming the AST shape both bind to. This is the
/// per-language successor of the <c>rules/</c> folder packs: the constructs belong to
/// the flat language they appear in, so they live in its file. At transpile time the
/// engine uses the <b>source</b> language's lifting and the <b>target</b> language's
/// lowering.
/// </summary>
public sealed record LanguageMapping
{
    /// <summary>The AST shape this mapping binds to (TryBlock, ArrayDeclaration, IndexedStore, IndexedLoad).</summary>
    public string Selector { get; init; } = string.Empty;

    public MappingLowering Lowering { get; init; } = new();

    public MappingLifting Lifting { get; init; } = new();
}
