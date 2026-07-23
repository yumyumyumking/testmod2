using System.Text.Json.Serialization;

namespace Transpiler.Core.Languages;

/// <summary>
/// Tier-1 genericity (SPEC §7.1): how one language is spelled. Loaded from
/// <c>languages/*.language.json</c> — one JSON file is the complete definition of a
/// language. Shipped defaults cover the placeholder grammar.
/// </summary>
public sealed class LanguageProfile
{
    public int SchemaVersion { get; init; } = 2;

    public string Kind { get; init; } = "language";

    public bool IsEnabled { get; init; } = true;

    /// <summary>
    /// The language identifier — the dictionary key everything else uses (engine API,
    /// workspace lookups, editor dropdown). Case-insensitive. RimWorld-style: drop a
    /// JSON file with a new name and a new language exists. The registry keys by this
    /// value, so nothing reassigns it after load.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public bool CaseSensitive { get; init; }

    public CommentStyle Comment { get; init; } = new();

    public KeywordTable Keywords { get; init; } = new();

    /// <summary>
    /// The 'recipe' JSON block: the program skeleton authored as named sections with
    /// kinds, header patterns/keywords, presence and containment. Required — a
    /// language without one is dropped at load with the reason in the diagnostic log.
    /// </summary>
    [JsonPropertyName("recipe")]
    public Dictionary<string, RecipeSection>? DeclaredRecipe { get; init; }

    /// <summary>
    /// Statement terminator characters tolerated at end of line: ";" / ";," / "" (or
    /// absent) for strictly newline-terminated statements.
    /// </summary>
    public string? Breakpoint { get; init; }

    /// <summary>
    /// The 'mappings' JSON block: this language's vendor-construct correspondences
    /// (lifting + lowering paired per construct), keyed by mapping id. Only meaningful
    /// for flat languages (<c>blockIf: false</c>) — structured languages express these
    /// constructs natively. Null when the language declares none.
    /// </summary>
    [JsonPropertyName("mappings")]
    public Dictionary<string, LanguageMapping>? DeclaredMappings { get; init; }

    /// <summary>
    /// The 'variables' JSON block: what variables this language can declare and how —
    /// named scope entries (keyword + engine kind + binding policy) and data-kind
    /// entries (spelling + point type). Null falls back to the CL-family
    /// <see cref="VariableModel.Default"/>.
    /// </summary>
    [JsonPropertyName("variables")]
    public VariableModel? DeclaredVariables { get; init; }

    private VariablePlan? _variables;

    /// <summary>
    /// The resolved variable model. Loader-validated for files; the build throws a
    /// precise <c>variables.&lt;entry&gt;: …</c> message for hand-built profiles.
    /// </summary>
    [JsonIgnore]
    public VariablePlan Variables => _variables ??= VariablePlan.For(DeclaredVariables ?? VariableModel.Default);

    private LanguageRecipe? _recipe;

    /// <summary>The declared recipe in resolved form (declaration order preserved); null without one.</summary>
    [JsonIgnore]
    public LanguageRecipe? Recipe =>
        DeclaredRecipe is null ? null : _recipe ??= new LanguageRecipe(DeclaredRecipe.ToList());

    private SectionPlan? _plan;

    /// <summary>
    /// The program-level grammar of this language as data — which sections exist, at
    /// which level, in what order, how often — built from the <see cref="Recipe"/>.
    /// The parser's ParseProgram interprets this plan. The loader validates recipe
    /// presence and consistency before a profile registers, so the throw below only
    /// guards profiles constructed in code without a recipe.
    /// </summary>
    [JsonIgnore]
    public SectionPlan Plan => _plan ??= SectionPlan.For(
        Recipe ?? throw new InvalidOperationException(
            $"Language '{Name}' declares no recipe — the 'recipe' block is required."));

    /// <summary>
    /// How this language delimits blocks: keyword-terminated (default) or wrapped in
    /// braces. Concrete syntax only — the AST and transformation passes are identical
    /// for both styles, so the two interconvert freely.
    /// </summary>
    public BlockStyle Blocks { get; init; } = new();

    public LabelStyle Labels { get; init; } = new();

    public LanguageCapabilities Capabilities { get; init; } = new();

    [JsonIgnore]
    public string SourceFile { get; set; } = "<built-in>";

    [JsonIgnore]
    public StringComparer NameComparer =>
        CaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    [JsonIgnore]
    public StringComparison NameComparison =>
        CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    // ------------------------------------------------------- built-in fallbacks
    // Resilience, not a config format: when languages/*.json are missing the engine
    // still transpiles CLX ⇄ CL. Defined by the same recipe/mappings model as the
    // shipped files (languages/cl.language.json, clx.language.json) and kept in sync
    // with them.

    /// <summary>Built-in CL profile used when no language file is present.</summary>
    public static LanguageProfile DefaultCl { get; } = new()
    {
        Name = "CL",
        DisplayName = "Generic CL (built-in)",
        Capabilities = new LanguageCapabilities
        {
            BlockIf = false,
            PointAllocation = true,
            AllocationField = "database",
        },
        DeclaredRecipe = new Dictionary<string, RecipeSection>
        {
            ["namespace"] = new()
            {
                Format = "SEQUENCE {name} ({hardware}; POINT {database})",
                Defaults = new Dictionary<string, string>
                {
                    ["hardware"] = "GENERIC",
                    ["database"] = "DB",
                },
                MustContain = new[] { "mainroutine" },
                CanContain = new[] { "variabledeclaration" },
            },
            ["mainroutine"] = new()
            {
                Start = "PHASE",
                End = "ENDPHASE",
                EndEmit = "ENDPHASE {name}",
                EndOptional = true,
                CanContain = new[] { "variabledeclaration", "subroutine" },
            },
            ["subroutine"] = new() { Start = "STEP", End = "END", EndEmit = "END {name}" },
            ["variabledeclaration"] = new(),
        },
        DeclaredVariables = BuiltInVariables(),
        DeclaredMappings = new Dictionary<string, LanguageMapping>
        {
            ["trycatch"] = new()
            {
                Selector = "TryBlock",
                Lowering = new MappingLowering
                {
                    Begin = "GUARD_BEGIN",
                    Middle = "GUARD_ONFAULT {faultVar}",
                    End = "GUARD_END",
                },
                Lifting = new MappingLifting
                {
                    Begin = "GUARD_BEGIN",
                    Middle = "GUARD_ONFAULT {faultVar:identifier}",
                    End = "GUARD_END",
                },
            },
            ["array-alloc"] = new()
            {
                Selector = "ArrayDeclaration",
                Lowering = new MappingLowering { Format = "ALLOC {name} SIZE {size}" },
                Lifting = new MappingLifting { Pattern = "ALLOC {name:identifier} SIZE {size:number}" },
            },
            ["array-store"] = new()
            {
                Selector = "IndexedStore",
                Lowering = new MappingLowering { Format = "STORE {array} IDX {index} VAL {value}" },
                Lifting = new MappingLifting { Pattern = "STORE {array:identifier} IDX {index:expression} VAL {value:expression}" },
            },
            ["array-load"] = new()
            {
                Selector = "IndexedLoad",
                Lowering = new MappingLowering { Format = "LOAD {dest} FROM {array} IDX {index}" },
                Lifting = new MappingLifting { Pattern = "LOAD {dest:identifier} FROM {array:identifier} IDX {index:expression}" },
            },
        },
    };

    /// <summary>Built-in CLX profile used when no language file is present.</summary>
    public static LanguageProfile DefaultClx { get; } = new()
    {
        Name = "CLX",
        DisplayName = "CLX (built-in)",
        Capabilities = new LanguageCapabilities { BlockIf = true },
        DeclaredRecipe = new Dictionary<string, RecipeSection>
        {
            ["namespace"] = new()
            {
                Format = "SEQUENCE {name} ({hardware}; POINT {database})",
                Defaults = new Dictionary<string, string>
                {
                    ["hardware"] = "GENERIC",
                    ["database"] = "DB",
                },
                MustContain = new[] { "mainroutine" },
                CanContain = new[] { "variabledeclaration" },
            },
            ["mainroutine"] = new()
            {
                Start = "PHASE",
                CanContain = new[] { "variabledeclaration", "subroutine" },
            },
            ["subroutine"] = new() { Start = "STEP", End = "END", EndEmit = "END {name}" },
            ["variabledeclaration"] = new(),
        },
        DeclaredVariables = BuiltInVariables(),
    };

    /// <summary>
    /// The CL-family variable model the built-ins declare explicitly, mirroring the
    /// shipped language files (external EXTERNAL, local LOCAL with optional bindings;
    /// numeric NUMBER→NN, boolean LOGICAL→FL).
    /// </summary>
    private static VariableModel BuiltInVariables() => new()
    {
        Scopes = new Dictionary<string, VariableScope>(StringComparer.OrdinalIgnoreCase)
        {
            ["external"] = new() { Keyword = "EXTERNAL" },
            ["local"] = new() { Keyword = "LOCAL", Binding = "optional" },
        },
        Kinds = new Dictionary<string, VariableKindSpec>(StringComparer.OrdinalIgnoreCase)
        {
            ["numeric"] = new() { Spelling = "NUMBER", Point = "NN" },
            ["boolean"] = new() { Spelling = "LOGICAL", Point = "FL" },
        },
    };
}
