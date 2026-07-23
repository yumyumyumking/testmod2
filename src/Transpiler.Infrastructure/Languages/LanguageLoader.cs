using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Transpiler.Infrastructure.Languages;

/// <summary>
/// RimWorld-style language registry loader: every <c>*.json</c> under the languages
/// folder that survives strong validation registers a language under its
/// <c>name</c>. One file is the complete definition — recipe (program skeleton),
/// keywords, capabilities, mappings — and the <c>recipe</c> block is required.
/// Anything invalid is <b>dropped with the reason in the diagnostic log</b> — a bad
/// file can never take the application down or block other languages. When two files
/// declare the same name, the later file (scan order) overwrites by default; pass
/// <c>overwriteDuplicates: false</c> to keep the first and report the clash instead.
/// </summary>
public sealed class LanguageLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly ILogger<LanguageLoader> _log;

    public LanguageLoader(ILogger<LanguageLoader> log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public LanguageLoadResult Load(string languagesFolder, bool overwriteDuplicates = true)
    {
        var profiles = new Dictionary<string, LanguageProfile>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        var skipped = 0;

        if (!Directory.Exists(languagesFolder))
        {
            _log.LogWarning("Languages folder not found: {Folder}; using built-in defaults.", languagesFolder);
            return new LanguageLoadResult(profiles, errors, skipped);
        }

        foreach (var file in Directory.EnumerateFiles(languagesFolder, "*.json", SearchOption.AllDirectories)
                     .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase))
        {
            string? text = null;
            try
            {
                text = File.ReadAllText(file);
                var profile = JsonSerializer.Deserialize<LanguageProfile>(text, SerializerOptions);
                if (profile is null)
                {
                    Drop(errors, file, "file is empty or not a JSON object.");
                    continue;
                }

                if (!profile.IsEnabled)
                {
                    skipped++;
                    continue;
                }

                var problems = Validate(profile);
                ReportRetiredFields(text, problems);
                if (problems.Count > 0)
                {
                    foreach (var problem in problems)
                    {
                        Drop(errors, file, problem);
                    }

                    continue; // strong validation: the whole language is dropped
                }

                profile.SourceFile = file;

                if (profiles.TryGetValue(profile.Name, out var existing))
                {
                    if (!overwriteDuplicates)
                    {
                        Drop(errors, file, $"language '{profile.Name}' is already defined by {existing.SourceFile}; duplicates are not allowed (overwriteDuplicates is off).");
                        continue;
                    }

                    _log.LogInformation("Language '{Name}' redefined by {File} (overrides {Previous}).",
                        profile.Name, Path.GetFileName(file), Path.GetFileName(existing.SourceFile));
                }

                profiles[profile.Name] = profile;
                _log.LogInformation("Registered language '{Name}' ({Display}) from {File}.",
                    profile.Name, profile.DisplayName, Path.GetFileName(file));
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                Drop(errors, file, DescribeUnreadable(text, ex));
            }
        }

        return new LanguageLoadResult(profiles, errors, skipped);
    }

    private void Drop(List<string> errors, string file, string reason)
    {
        var message = $"{file}: {reason}";
        errors.Add(message);
        _log.LogWarning("Language dropped — {Reason}", message);
    }

    /// <summary>
    /// The drop reason for a file the typed deserialize rejected. A retired 1.6-era
    /// rule pack is the one likely-legitimate file that lands here (its `mappings`
    /// is an array, the language schema's is an object), so it gets a migration
    /// message instead of a raw serializer error.
    /// </summary>
    private static string DescribeUnreadable(string? text, Exception ex)
    {
        if (ex is not JsonException || text is null)
        {
            return ex.Message;
        }

        try
        {
            using var document = JsonDocument.Parse(text, DocumentOptions);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (Insensitive(property.Name, "kind") &&
                        property.Value.ValueKind == JsonValueKind.String &&
                        Insensitive(property.Value.GetString() ?? string.Empty, "mapping"))
                    {
                        return "kind is 'mapping' — this is a retired rule-pack file; rule packs were removed in 1.7.0. " +
                               "Move each mapping into the flat language's file under 'mappings' (lower → lowering, lift → lifting).";
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Not even valid JSON; the original message is the best description.
        }

        return ex.Message;
    }

    /// <summary>
    /// Retired configuration surfaces (removed in 1.7.0). The deserializer ignores
    /// unknown JSON fields, so without this check an old file would silently lose its
    /// section configuration and then fail with only "missing 'recipe'" — these
    /// messages point at the exact stale block instead.
    /// </summary>
    private static void ReportRetiredFields(string json, List<string> problems)
    {
        using var document = JsonDocument.Parse(json, DocumentOptions);

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (Insensitive(property.Name, "header"))
            {
                problems.Add("the 'header' block was retired in 1.7.0 — spell the sections in the 'recipe' block instead (see languages/cl.language.json for the shape).");
            }
            else if (Insensitive(property.Name, "language"))
            {
                problems.Add("the 'language' field was retired in 1.7.0 — name the language with 'name'.");
            }
            else if (Insensitive(property.Name, "keywords") && property.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var keyword in property.Value.EnumerateObject())
                {
                    if (Insensitive(keyword.Name, "sequence") || Insensitive(keyword.Name, "phase") ||
                        Insensitive(keyword.Name, "step") || Insensitive(keyword.Name, "stepEnd"))
                    {
                        problems.Add($"keywords.{keyword.Name} was retired in 1.7.0 — section spellings live in the 'recipe' block (start/end or format).");
                    }
                    else if (Insensitive(keyword.Name, "point"))
                    {
                        problems.Add("keywords.point was retired in 1.8.0 — the header line's shape (including any literal like POINT) is spelled entirely by the namespace section's 'format' pattern.");
                    }
                    else if (Insensitive(keyword.Name, "externalDecl") || Insensitive(keyword.Name, "localDecl") ||
                             Insensitive(keyword.Name, "typeNumeric") || Insensitive(keyword.Name, "typeLogical") ||
                             Insensitive(keyword.Name, "numericPoint") || Insensitive(keyword.Name, "flagPoint"))
                    {
                        problems.Add($"keywords.{keyword.Name} was retired in 1.9.0 — declaration spellings live in the 'variables' block (scopes: keyword; kinds: spelling/point).");
                    }
                }
            }
            else if (Insensitive(property.Name, "capabilities") && property.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var capability in property.Value.EnumerateObject())
                {
                    if (Insensitive(capability.Name, "supportsTypes"))
                    {
                        problems.Add("capabilities.supportsTypes was retired in 1.9.0 — kind support is derived from the 'variables.kinds' block (declare no kinds for an untyped language).");
                    }
                }
            }
        }
    }

    private static bool Insensitive(string name, string expected) =>
        string.Equals(name, expected, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Strong validation. Any failure drops the language: a half-valid language would
    /// produce confusing parse behavior, so it is all-or-nothing.
    /// </summary>
    private static List<string> Validate(LanguageProfile profile)
    {
        var problems = new List<string>();

        if (profile.SchemaVersion != 2)
        {
            problems.Add($"unsupported schemaVersion {profile.SchemaVersion} (expected 2).");
        }

        // A rule pack dropped into the languages folder would otherwise deserialize
        // into a default-filled profile and register a bogus language.
        if (!string.Equals(profile.Kind, "language", StringComparison.OrdinalIgnoreCase))
        {
            problems.Add($"kind is '{profile.Kind}' — this folder only loads files with kind 'language'.");
        }

        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            problems.Add("missing 'name' — the language identifier is required.");
        }

        if (profile.DeclaredRecipe is null)
        {
            problems.Add("missing 'recipe' — a language file must declare its program skeleton (sections, containment, header spellings) in the 'recipe' block.");
        }

        if (string.IsNullOrEmpty(profile.Comment.Line))
        {
            problems.Add("comment.line must not be empty.");
        }

        if (profile.Blocks.Style == BlockDelimiterStyle.Braces &&
            (string.IsNullOrEmpty(profile.Blocks.Open) || string.IsNullOrEmpty(profile.Blocks.Close)))
        {
            problems.Add("blocks.open and blocks.close are required for the Braces style.");
        }

        if (profile.Capabilities.LocalCapacity < 1)
        {
            problems.Add($"capabilities.localCapacity must be at least 1 (was {profile.Capabilities.LocalCapacity}).");
        }

        if (string.IsNullOrEmpty(profile.Labels.Suffix))
        {
            problems.Add("labels.suffix must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(profile.Labels.GeneratedPrefix))
        {
            // The verifier's read-back and the lifter's counter recognition key on
            // this prefix; a blank one would silently fail every verification.
            problems.Add("labels.generatedPrefix must not be empty.");
        }

        // Every keyword-table spelling must be non-blank: the parser dispatches on
        // these, and a nulled-out entry would otherwise surface as a crash or a
        // phrase that "matches" nothing. Reflection keeps the list complete when
        // the table grows.
        var kw = profile.Keywords;
        foreach (var property in typeof(KeywordTable).GetProperties())
        {
            if (property.PropertyType == typeof(string) &&
                string.IsNullOrWhiteSpace(property.GetValue(kw) as string))
            {
                problems.Add($"keywords.{Camel(property.Name)} must not be empty.");
            }
        }

        if (profile.Breakpoint is { } breakpoint && breakpoint.Any(static c => c is not (';' or ',')))
        {
            problems.Add($"breakpoint '{breakpoint}' may only contain ';' and ',' (or be empty for strictly newline-terminated statements).");
        }

        ValidateLanguageMappings(profile, problems);

        if (profile.Recipe is { } recipe)
        {
            ValidateRecipeSections(profile, recipe, problems);
        }

        // The plan builds are the single source of truth for their rules — recipe
        // containment/presence/nesting, and the variable model's scopes and kinds; a
        // failure becomes this language's drop reason verbatim.
        SectionPlan? plan = null;
        try
        {
            plan = profile.DeclaredRecipe is null ? null : profile.Plan;
        }
        catch (InvalidOperationException ex)
        {
            problems.Add(ex.Message);
        }

        VariablePlan? variables = null;
        try
        {
            variables = profile.Variables;
        }
        catch (InvalidOperationException ex)
        {
            problems.Add(ex.Message);
        }

        if (variables is not null)
        {
            ValidateVariables(profile, plan, variables, problems);
        }

        if (plan is not null)
        {
            ValidateAllocation(profile, plan, variables, problems);
            ValidateDispatchDistinctness(profile, plan, variables, kw, problems);
        }

        return problems;
    }

    /// <summary>
    /// Cross-checks between the variable model and the rest of the profile: a recipe
    /// that hosts declaration sections needs at least one declarable scope, and a
    /// point-allocation target must give every declared kind a point type (a local of
    /// a point-less kind could never be allocated).
    /// </summary>
    private static void ValidateVariables(
        LanguageProfile profile, SectionPlan? plan, VariablePlan variables, List<string> problems)
    {
        var hasDeclarationSection = plan is not null &&
            (plan.FileSections.Concat(plan.MainRoutineSections).Any(static rule => rule.Content == SectionContent.Declarations));
        if (hasDeclarationSection && variables.Scopes.Count == 0)
        {
            problems.Add("variables.scopes: the recipe declares a variabledeclaration section but no scope is declarable — declare at least one scope (or drop the section).");
        }

        if (profile.Capabilities.PointAllocation)
        {
            foreach (var kind in variables.Kinds.Where(static rule => rule.Point is null))
            {
                problems.Add($"variables.{kind.Name}: a 'point' type is required with capabilities.pointAllocation — locals of kind '{kind.Name}' could never be allocated.");
            }
        }
    }

    /// <summary>
    /// Point allocation is pure configuration: the capability names the header field
    /// that carries the point area, and the namespace section's defaults must supply
    /// that field so headerless sources allocate consistently with the header the
    /// emitter synthesizes.
    /// </summary>
    private static void ValidateAllocation(
        LanguageProfile profile, SectionPlan plan, VariablePlan? variables, List<string> problems)
    {
        if (!profile.Capabilities.PointAllocation)
        {
            if (!string.IsNullOrWhiteSpace(profile.Capabilities.AllocationField))
            {
                problems.Add("capabilities.allocationField has no effect without capabilities.pointAllocation — remove one.");
            }

            return;
        }

        if (variables is { Kinds.Count: 0 })
        {
            problems.Add("capabilities.pointAllocation requires at least one entry in variables.kinds — allocation maps each kind to a point type.");
        }

        var field = profile.Capabilities.AllocationField;
        if (string.IsNullOrWhiteSpace(field))
        {
            problems.Add("capabilities.allocationField is required with pointAllocation — name the header field that carries the point area.");
            return;
        }

        if (plan.Namespace is not { } ns)
        {
            problems.Add("capabilities.pointAllocation requires a namespace (file-header) section — the point area is named by a header field.");
            return;
        }

        // Ordinal: header-field lookups at run time are exact-spelling.
        var captured = ns.StartPattern is { } pattern &&
            SectionPatterns.Placeholders(pattern).Contains(field, StringComparer.Ordinal);
        if (!captured)
        {
            problems.Add($"capabilities.allocationField '{field}' is not a capture of the namespace section's format (spelling must match the capture exactly).");
        }

        if (!ns.Defaults.ContainsKey(field))
        {
            problems.Add($"recipe.{(ns.Name.Length > 0 ? ns.Name : "namespace")}.defaults must supply '{field}' — headerless sources synthesize a header, and allocation needs the area name.");
        }
    }

    /// <summary>
    /// Distinct spellings for everything the parser dispatches on, or parsing is
    /// ambiguous: every rule's start/end dispatch word (keyword, or a pattern's
    /// leading literal), the declaration keywords (the section-plan grammar
    /// recognizes them as alternatives at every section level), and the statement
    /// dispatch keywords.
    /// </summary>
    private static void ValidateDispatchDistinctness(
        LanguageProfile profile, SectionPlan plan, VariablePlan? variables, KeywordTable kw, List<string> problems)
    {
        var rules = plan.FileSections.Concat(plan.MainRoutineSections).Distinct().ToList();
        if (plan.Namespace is { } ns)
        {
            rules.Add(ns);
        }

        var dispatch = rules
            .SelectMany(static rule => new[]
            {
                rule.UsesPatterns ? rule.StartFirstWord : rule.Delimiters.Start,
                rule.EndPattern is not null ? rule.EndFirstWord : rule.Delimiters.End,
            })
            .Where(static word => !string.IsNullOrWhiteSpace(word))
            .Select(static word => word!)
            .Concat((variables?.Scopes ?? Array.Empty<VariableScopeRule>()).Select(static scope => scope.Keyword))
            .Concat(new[] { kw.If, kw.Set, kw.Reset, kw.Goto, kw.Call, kw.Return }
                .Where(static word => !string.IsNullOrWhiteSpace(word)))
            .ToList();

        if (dispatch.Distinct(profile.NameComparer).Count() != dispatch.Count)
        {
            problems.Add("section delimiters (recipe sections), declaration-scope keywords (variables.scopes) and statement keywords (if/set/reset/goto/call/return) must all be distinct.");
        }
    }

    private static readonly Regex TypedPlaceholder = new(@"\{(\w+)(?::(\w+))?\}", RegexOptions.CultureInvariant);

    /// <summary>
    /// Field-level recipe checks with precise paths (recipe.&lt;section&gt;.&lt;field&gt;):
    /// patterns must compile, capture types must be known, sections that name their
    /// instances must capture {name}, and emit templates may only reference captured
    /// placeholders. Containment/presence/nesting rules live in the plan build.
    /// </summary>
    private static void ValidateRecipeSections(LanguageProfile profile, LanguageRecipe recipe, List<string> problems)
    {
        var braces = profile.Blocks.Style == BlockDelimiterStyle.Braces;
        foreach (var (name, section) in recipe.Entries.Select(static e => (e.Key, e.Value)))
        {
            void Problem(string field, string message) => problems.Add($"recipe.{name}.{field}: {message}");

            var kind = LanguageRecipe.ResolveKind(name, section);
            var namesItsInstances = kind is SectionContent.Namespace or SectionContent.MainRoutine
                or SectionContent.SubRoutine or SectionContent.Function or SectionContent.Handler;

            var formatCaptures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (section.Format is { } format)
            {
                if (braces)
                {
                    Problem("format", "pattern headers are not supported with the Braces block style; use start/end keyword shorthand.");
                }

                ValidatePattern(format, "format", terminator: false, profile.CaseSensitive, Problem);
                foreach (var capture in SectionPatterns.Placeholders(format))
                {
                    formatCaptures.Add(capture);
                }

                if (namesItsInstances && !formatCaptures.Contains(SectionPatterns.NameCapture))
                {
                    Problem("format", "must capture {name} — this section kind names its instances.");
                }

                if (section.Emit is { } emit)
                {
                    foreach (var placeholder in SectionPatterns.Placeholders(emit)
                                 .Where(p => !formatCaptures.Contains(p)))
                    {
                        Problem("emit", $"placeholder '{{{placeholder}}}' is not captured by format.");
                    }
                }

                // Exact-spelling check: runtime lookups (emit render, verifier fill,
                // allocation) are ordinal, so a case-variant default would silently
                // never apply — reject it here instead.
                var exactCaptures = new HashSet<string>(SectionPatterns.Placeholders(format), StringComparer.Ordinal);
                foreach (var key in section.Defaults.Keys.Where(k => !exactCaptures.Contains(k)))
                {
                    Problem("defaults", $"'{key}' is not a capture of this section's format — a default can only fill a field the pattern defines, spelled exactly as captured.");
                }
            }

            if (section.EndFormat is { } endFormat)
            {
                ValidatePattern(endFormat, "endFormat", terminator: true, profile.CaseSensitive, Problem);
                if (section.EndEmit is null)
                {
                    Problem("endEmit", "required alongside endFormat — the terminator's emitted spelling must be explicit.");
                }
            }

            if (section.EndEmit is { } endEmit)
            {
                var known = new HashSet<string>(formatCaptures, StringComparer.OrdinalIgnoreCase)
                {
                    SectionPatterns.NameCapture,
                };
                if (section.EndFormat is { } terminatorFormat)
                {
                    foreach (var capture in SectionPatterns.Placeholders(terminatorFormat))
                    {
                        known.Add(capture);
                    }
                }

                foreach (var placeholder in SectionPatterns.Placeholders(endEmit).Where(p => !known.Contains(p)))
                {
                    Problem("endEmit", $"placeholder '{{{placeholder}}}' is captured by neither format nor endFormat.");
                }
            }
        }
    }

    /// <summary>What each selector's lifting must capture (and all its lowering may reference).</summary>
    private static readonly Dictionary<string, string[]> SelectorCaptures = new(StringComparer.Ordinal)
    {
        [MappingSelectors.TryBlock] = new[] { "faultVar" },
        [MappingSelectors.ArrayDeclaration] = new[] { "name", "size" },
        [MappingSelectors.IndexedStore] = new[] { "array", "index", "value" },
        [MappingSelectors.IndexedLoad] = new[] { "dest", "array", "index" },
    };

    /// <summary>
    /// Field-level checks of the language's own <c>mappings</c> block, with precise
    /// mappings.&lt;id&gt;.&lt;side&gt;.&lt;field&gt; paths. Stronger than the legacy
    /// folder validation: selectors must capture what their AST node needs, lowering
    /// may only reference those captures, and a structured language declaring
    /// mappings is rejected outright (nothing would ever run them).
    /// </summary>
    private static void ValidateLanguageMappings(LanguageProfile profile, List<string> problems)
    {
        if (profile.DeclaredMappings is not { Count: > 0 } declared)
        {
            return;
        }

        if (profile.Capabilities.BlockIf)
        {
            problems.Add(
                "mappings: only flat languages (capabilities.blockIf: false) lower/lift vendor constructs — " +
                "a structured language expresses TRY/CATCH and arrays natively, so these mappings would never run.");
        }

        var selectorsSeen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (id, mapping) in declared.Select(static e => (e.Key, e.Value)))
        {
            void Problem(string field, string message) => problems.Add($"mappings.{id}.{field}: {message}");

            if (!MappingSelectors.All.Contains(mapping.Selector))
            {
                Problem("selector", $"unknown selector '{mapping.Selector}'. Known: {string.Join(", ", MappingSelectors.All)}.");
                continue;
            }

            if (selectorsSeen.TryGetValue(mapping.Selector, out var previous))
            {
                Problem("selector", $"selector '{mapping.Selector}' is already bound by mapping '{previous}' — one mapping per selector per language.");
                continue;
            }

            selectorsSeen[mapping.Selector] = id;
            var allowed = SelectorCaptures[mapping.Selector];

            if (MappingSelectors.IsFrame(mapping.Selector))
            {
                RequireTemplate(mapping.Lowering.Begin, "lowering.begin", allowed, Problem);
                RequireTemplate(mapping.Lowering.Middle, "lowering.middle", allowed, Problem);
                RequireTemplate(mapping.Lowering.End, "lowering.end", allowed, Problem);
                if (mapping.Lowering.Format is not null)
                {
                    Problem("lowering.format", "frame selectors use begin/middle/end, not format.");
                }

                RequireLiftPattern(mapping.Lifting.Begin, "lifting.begin", requiredCaptures: null, profile, Problem);
                RequireLiftPattern(mapping.Lifting.Middle, "lifting.middle", requiredCaptures: allowed, profile, Problem);
                RequireLiftPattern(mapping.Lifting.End, "lifting.end", requiredCaptures: null, profile, Problem);
                if (mapping.Lifting.Pattern is not null)
                {
                    Problem("lifting.pattern", "frame selectors use begin/middle/end, not pattern.");
                }
            }
            else
            {
                RequireTemplate(mapping.Lowering.Format, "lowering.format", allowed, Problem);
                if (mapping.Lowering.Begin is not null || mapping.Lowering.Middle is not null || mapping.Lowering.End is not null)
                {
                    Problem("lowering", "statement selectors use format, not begin/middle/end.");
                }

                RequireLiftPattern(mapping.Lifting.Pattern, "lifting.pattern", requiredCaptures: allowed, profile, Problem);
                if (mapping.Lifting.Begin is not null || mapping.Lifting.Middle is not null || mapping.Lifting.End is not null)
                {
                    Problem("lifting", "statement selectors use pattern, not begin/middle/end.");
                }
            }
        }
    }

    private static void RequireTemplate(string? template, string field, string[] allowedPlaceholders, Action<string, string> problem)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            problem(field, "is required for this selector.");
            return;
        }

        foreach (var placeholder in MarkerTemplate.PlaceholderNames(template)
                     .Where(p => !allowedPlaceholders.Contains(p, StringComparer.Ordinal)))
        {
            problem(field, $"placeholder '{{{placeholder}}}' is not a capture of this selector " +
                $"(available: {string.Join(", ", allowedPlaceholders.Select(static c => "{" + c + "}"))}).");
        }
    }

    private static void RequireLiftPattern(
        string? pattern, string field, string[]? requiredCaptures, LanguageProfile profile, Action<string, string> problem)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            problem(field, "is required for this selector.");
            return;
        }

        foreach (Match placeholder in TypedPlaceholder.Matches(pattern))
        {
            if (placeholder.Groups[2].Success &&
                placeholder.Groups[2].Value.ToLowerInvariant() is not ("identifier" or "number" or "expression"))
            {
                problem(field, $"capture '{{{placeholder.Groups[1].Value}}}' has unknown type " +
                    $"'{placeholder.Groups[2].Value}' — use identifier, number or expression.");
            }
        }

        try
        {
            _ = MarkerTemplate.CompilePattern(pattern);
        }
        catch (ArgumentException ex)
        {
            problem(field, $"pattern does not compile: {ex.Message}");
            return;
        }

        if (requiredCaptures is not null)
        {
            var captured = MarkerTemplate.PlaceholderNames(pattern);
            foreach (var required in requiredCaptures.Where(c => !captured.Contains(c, StringComparer.Ordinal)))
            {
                problem(field, $"must capture '{{{required}}}' — the lift pass rebuilds the node from it.");
            }
        }
    }

    private static void ValidatePattern(
        string pattern, string field, bool terminator, bool caseSensitive, Action<string, string> problem)
    {
        foreach (Match placeholder in TypedPlaceholder.Matches(pattern))
        {
            if (placeholder.Groups[2].Success &&
                placeholder.Groups[2].Value.ToLowerInvariant() is not ("identifier" or "number" or "expression"))
            {
                problem(field, $"capture '{{{placeholder.Groups[1].Value}}}' has unknown type " +
                    $"'{placeholder.Groups[2].Value}' — use identifier, number or expression.");
            }
        }

        try
        {
            _ = terminator
                ? SectionPatterns.CompileTerminator(pattern, caseSensitive)
                : SectionPatterns.CompileHeader(pattern, caseSensitive);
        }
        catch (ArgumentException ex)
        {
            problem(field, $"pattern does not compile: {ex.Message}");
        }
    }

    private static string Camel(string slot) =>
        slot.Length == 0 ? slot : char.ToLowerInvariant(slot[0]) + slot[1..];
}
