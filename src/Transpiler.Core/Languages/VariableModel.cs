namespace Transpiler.Core.Languages;

/// <summary>
/// The <c>variables</c> JSON block: what variables this language can declare and how.
/// <see cref="Scopes"/> are named entries (like recipe sections) binding a spelling to
/// an engine scope kind plus a point-binding policy; <see cref="Kinds"/> bind data-kind
/// spellings and point types to the engine's kind vocabulary. Absent block = the
/// CL-family default (<see cref="Default"/>). Resolution and validation live in
/// <see cref="VariablePlan"/>.
/// </summary>
public sealed class VariableModel
{
    public Dictionary<string, VariableScope> Scopes { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, VariableKindSpec> Kinds { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The CL-family model used when a language declares no 'variables' block.</summary>
    public static VariableModel Default { get; } = new()
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

/// <summary>
/// One declarable scope: its keyword spelling, the engine scope kind it binds to
/// (<see cref="Kind"/> — inferred from the entry's name when that is an alias:
/// external / local / locallyscoped / localcanref), and its point-binding policy
/// ("forbidden" / "optional" / "required"; default derived from the alias —
/// locallyscoped forbids, localcanref and plain local permit).
/// </summary>
public sealed record VariableScope
{
    public string? Kind { get; init; }

    public string Keyword { get; init; } = string.Empty;

    public string? Binding { get; init; }
}

/// <summary>
/// One declarable data kind: how the type annotation is spelled
/// (<c>: NUMBER</c>), and — for languages whose locals live in point areas — which
/// point type carries it (<c>NN</c>). The engine kind it binds to is inferred from
/// the entry's name (numeric / boolean / byte and aliases).
/// </summary>
public sealed record VariableKindSpec
{
    public string Spelling { get; init; } = string.Empty;

    public string? Point { get; init; }
}
