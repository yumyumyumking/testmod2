namespace Transpiler.Core.Languages;

/// <summary>How a scope entry treats point bindings on its declarations.</summary>
public enum BindingPolicy
{
    Forbidden,
    Optional,
    Required,
}

/// <summary>One resolved scope entry of a language's variable model.</summary>
public sealed record VariableScopeRule(string Name, VariableScopeKind Kind, string Keyword, BindingPolicy Binding);

/// <summary>One resolved kind entry of a language's variable model.</summary>
public sealed record VariableKindRule(string Name, VariableKind Kind, string Spelling, string? Point);

/// <summary>
/// A language's variable model in resolved form — the table the parser, binder,
/// emitter, allocation pass and verifier consult. Built from the authored
/// <see cref="VariableModel"/>; building throws <see cref="InvalidOperationException"/>
/// with a precise <c>variables.&lt;entry&gt;: …</c> message on an inconsistent model,
/// and the loader converts any failure into a drop-with-reason.
/// </summary>
public sealed class VariablePlan
{
    private VariablePlan(IReadOnlyList<VariableScopeRule> scopes, IReadOnlyList<VariableKindRule> kinds)
    {
        Scopes = scopes;
        Kinds = kinds;
    }

    /// <summary>Declarable scopes, in declaration order (recognition and emission priority).</summary>
    public IReadOnlyList<VariableScopeRule> Scopes { get; }

    /// <summary>Declarable data kinds, in declaration order.</summary>
    public IReadOnlyList<VariableKindRule> Kinds { get; }

    /// <summary>True when this language can annotate the kind at all (some kind has a spelling).</summary>
    public bool SupportsKinds => Kinds.Count > 0;

    /// <summary>True when the target can represent <paramref name="kind"/> (declares an entry for it).</summary>
    public bool Supports(VariableKind kind) => Kinds.Any(rule => rule.Kind == kind);

    /// <summary>The kind rule for an engine kind; null when this language has none.</summary>
    public VariableKindRule? KindRuleFor(VariableKind kind) => Kinds.FirstOrDefault(rule => rule.Kind == kind);

    /// <summary>The kind whose point-type spelling is <paramref name="pointType"/>; null when none matches.</summary>
    public VariableKindRule? KindForPoint(string pointType, StringComparison comparison) =>
        Kinds.FirstOrDefault(rule => rule.Point is { } point && string.Equals(point, pointType, comparison));

    /// <summary>
    /// The scope entry a declaration of <paramref name="kind"/> is written as: the
    /// first entry of that engine kind whose binding policy admits the declaration's
    /// bound/unbound state, else the first entry of the kind at all (the emitter then
    /// drops a forbidden binding), else null — the scope is unrepresentable here.
    /// </summary>
    public VariableScopeRule? ScopeForEmit(VariableScopeKind kind, bool bound)
    {
        VariableScopeRule? fallback = null;
        foreach (var scope in Scopes)
        {
            if (scope.Kind != kind)
            {
                continue;
            }

            fallback ??= scope;
            var admits = bound ? scope.Binding != BindingPolicy.Forbidden : scope.Binding != BindingPolicy.Required;
            if (admits)
            {
                return scope;
            }
        }

        return fallback;
    }

    // ------------------------------------------------------------------ building

    public static VariablePlan For(VariableModel model)
    {
        var scopes = new List<VariableScopeRule>();
        foreach (var (name, scope) in model.Scopes.Select(static e => (e.Key, e.Value)))
        {
            var alias = scope.Kind ?? name;
            var kind = ScopeKindFromAlias(alias)
                ?? throw Invalid(name, scope.Kind is null
                    ? $"entry name is not a known scope kind; add a \"kind\" field (known: {string.Join(", ", ScopeAliases)})."
                    : $"unknown scope kind '{scope.Kind}' (known: {string.Join(", ", ScopeAliases)}).");

            if (string.IsNullOrWhiteSpace(scope.Keyword))
            {
                throw Invalid(name, "'keyword' is required — the spelling this scope's declarations start with.");
            }

            var binding = scope.Binding is { } declared
                ? ParseBinding(name, declared)
                : DefaultBinding(alias);

            if (kind == VariableScopeKind.External && binding == BindingPolicy.Required)
            {
                throw Invalid(name, "an external scope cannot require point bindings — points bind program-owned locals.");
            }

            scopes.Add(new VariableScopeRule(name, kind, scope.Keyword.Trim(), binding));
        }

        foreach (var group in scopes.GroupBy(static s => s.Keyword, StringComparer.OrdinalIgnoreCase).Where(static g => g.Count() > 1))
        {
            throw Invalid(string.Join("/", group.Select(static s => s.Name)),
                $"more than one scope is spelled '{group.Key}' — scope keywords must be distinct.");
        }

        var kinds = new List<VariableKindRule>();
        foreach (var (name, spec) in model.Kinds.Select(static e => (e.Key, e.Value)))
        {
            var kind = KindFromAlias(name)
                ?? throw Invalid(name, $"unknown kind (known: {string.Join(", ", KindAliases)}).");

            if (kinds.Any(rule => rule.Kind == kind))
            {
                throw Invalid(name, $"kind '{kind}' is declared more than once.");
            }

            if (string.IsNullOrWhiteSpace(spec.Spelling))
            {
                throw Invalid(name, "'spelling' is required — how the kind annotation is written.");
            }

            var point = string.IsNullOrWhiteSpace(spec.Point) ? null : spec.Point.Trim();
            kinds.Add(new VariableKindRule(name, kind, spec.Spelling.Trim(), point));
        }

        foreach (var group in kinds
                     .Where(static k => k.Point is not null)
                     .GroupBy(static k => k.Point!, StringComparer.OrdinalIgnoreCase)
                     .Where(static g => g.Count() > 1))
        {
            throw Invalid(string.Join("/", group.Select(static k => k.Name)),
                $"more than one kind maps to point type '{group.Key}' — point types must be distinct.");
        }

        foreach (var group in kinds.GroupBy(static k => k.Spelling, StringComparer.OrdinalIgnoreCase).Where(static g => g.Count() > 1))
        {
            throw Invalid(string.Join("/", group.Select(static k => k.Name)),
                $"more than one kind is spelled '{group.Key}' — kind spellings must be distinct.");
        }

        return new VariablePlan(scopes, kinds);
    }

    /// <summary>
    /// Scope aliases: the engine vocabulary plus the descriptive taxonomy names.
    /// "locallyscoped" defaults its binding policy to Forbidden and "localcanref" to
    /// Optional, so the taxonomy is writable directly as entry names.
    /// </summary>
    public static VariableScopeKind? ScopeKindFromAlias(string alias) => alias.Trim().ToLowerInvariant() switch
    {
        "external" => VariableScopeKind.External,
        "local" or "locallyscoped" or "localcanref" => VariableScopeKind.Local,
        _ => null,
    };

    private static BindingPolicy DefaultBinding(string alias) => alias.Trim().ToLowerInvariant() switch
    {
        "locallyscoped" => BindingPolicy.Forbidden,
        _ => BindingPolicy.Optional,
    };

    private static BindingPolicy ParseBinding(string entry, string value) => value.Trim().ToLowerInvariant() switch
    {
        "forbidden" => BindingPolicy.Forbidden,
        "optional" => BindingPolicy.Optional,
        "required" => BindingPolicy.Required,
        _ => throw Invalid(entry, $"binding is '{value}' — expected 'forbidden', 'optional' or 'required'."),
    };

    public static VariableKind? KindFromAlias(string alias) => alias.Trim().ToLowerInvariant() switch
    {
        "numeric" or "number" => VariableKind.Numeric,
        "boolean" or "logical" or "flag" => VariableKind.Boolean,
        "byte" => VariableKind.Byte,
        _ => null,
    };

    private static readonly string[] ScopeAliases = { "external", "local", "locallyscoped", "localcanref" };

    private static readonly string[] KindAliases = { "numeric", "boolean", "byte" };

    private static InvalidOperationException Invalid(string entry, string message) =>
        new($"variables.{entry}: {message}");
}
