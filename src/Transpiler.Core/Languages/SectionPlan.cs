namespace Transpiler.Core.Languages;

/// <summary>
/// The program skeleton as data: which sections a program of this language is made
/// of, at which level each lives, in what priority order they are recognized, and
/// how often each may appear. <c>ParseProgram</c> is an interpreter over this plan —
/// it never hard-codes a header → declarations → routines walk.
///
/// The shape of a program is:
///
/// <code>
///   program   := namespaceHeader?  fileItem*  namespaceEnd?
///   fileItem  := one of FileSections  (first matching rule wins; Statements last — it matches anything)
///   routine   := mainRoutineHeader  routineItem*  mainRoutineEnd?
///   routineItem := one of MainRoutineSections
/// </code>
///
/// Built from a <see cref="LanguageRecipe"/> (the authored JSON form) — containment
/// is explicit (mustContain/canContain), and an absent section shifts its content up
/// a level (implicit wrappers). Ordering and cardinality are data here — a language
/// family that needs a different skeleton changes JSON, not the parser.
///
/// Plan building throws <see cref="InvalidOperationException"/> with a precise message
/// on an inconsistent recipe; the loader runs the same construction at startup and
/// converts any failure into a drop-with-reason, so end users see the message in the
/// diagnostic log rather than an exception.
/// </summary>
public sealed class SectionPlan
{
    private SectionPlan(
        SectionRule? @namespace,
        IReadOnlyList<SectionRule> fileSections,
        IReadOnlyList<SectionRule> mainRoutineSections)
    {
        Namespace = @namespace;
        FileSections = fileSections;
        MainRoutineSections = mainRoutineSections;
    }

    /// <summary>
    /// The outermost container and file header (SEQUENCE / MODULE); null when the
    /// language has none (MATLAB). Its start line is the file prologue — required
    /// exactly once when present; with a terminator it wraps the whole program.
    /// </summary>
    public SectionRule? Namespace { get; }

    /// <summary>The alternatives at file level, in recognition-priority order.</summary>
    public IReadOnlyList<SectionRule> FileSections { get; }

    /// <summary>The alternatives inside one main routine, in recognition-priority order.</summary>
    public IReadOnlyList<SectionRule> MainRoutineSections { get; }

    /// <summary>Every routine-bearing rule of either level (for boundary predicates).</summary>
    public IEnumerable<SectionRule> RoutineRules =>
        FileSections.Concat(MainRoutineSections).Where(static rule => rule.IsRoutine);

    /// <summary>The rule holding <paramref name="content"/>, searching both levels; null when the plan has none.</summary>
    public SectionRule? FindRule(SectionContent content) =>
        FileSections.Concat(MainRoutineSections).FirstOrDefault(rule => rule.Content == content);

    // ----------------------------------------------------------- recipe building

    /// <summary>
    /// Builds the plan from an authored recipe. Containment is explicit here: the
    /// namespace section's lists are the file level (when a namespace exists),
    /// uncontained sections sit at file level otherwise, and the main-routine
    /// section's lists are its own level. Cardinality comes from
    /// mustContain (at least one per host) vs canContain (any number), reconciled
    /// with each section's own <c>presence</c> field.
    /// </summary>
    public static SectionPlan For(LanguageRecipe recipe)
    {
        // Resolve every section's kind first; everything else keys off it.
        var resolved = new List<(string Name, RecipeSection Section, SectionContent Content)>();
        foreach (var (name, section) in recipe.Entries.Select(static e => (e.Key, e.Value)))
        {
            var content = LanguageRecipe.ResolveKind(name, section)
                ?? throw Invalid(name, section.Kind is null
                    ? $"section name is not a known kind; add a \"kind\" field (known kinds: {string.Join(", ", LanguageRecipe.KnownKinds)})."
                    : $"unknown kind '{section.Kind}' (known kinds: {string.Join(", ", LanguageRecipe.KnownKinds)}).");
            resolved.Add((name, section, content));
        }

        foreach (var group in resolved.GroupBy(static r => r.Content).Where(static g => g.Count() > 1))
        {
            throw Invalid(string.Join("/", group.Select(static r => r.Name)),
                $"more than one section carries kind '{group.Key}'; each kind may appear once.");
        }

        var namespaceEntry = resolved.FirstOrDefault(static r => r.Content == SectionContent.Namespace);
        var mainRoutineEntry = resolved.FirstOrDefault(static r => r.Content == SectionContent.MainRoutine);

        if (namespaceEntry.Section is { } ns &&
            string.Equals(ns.Presence?.Trim(), "optional", StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid(namespaceEntry.Name,
                "namespace presence must be 'required' — an optional file wrapper is not supported.");
        }

        // Only containers may host sections.
        foreach (var (name, section, content) in resolved)
        {
            if (content is not (SectionContent.Namespace or SectionContent.MainRoutine) &&
                (section.MustContain.Count > 0 || section.CanContain.Count > 0))
            {
                throw Invalid(name,
                    $"a '{content}' section cannot contain other sections — nesting deeper than " +
                    "namespace → mainroutine → subroutine is a tier-3 (code) extension, not a recipe feature.");
            }
        }

        var hosts = BuildHostIndex(resolved, namespaceEntry.Name, mainRoutineEntry.Name);

        var fileSections = BuildLevel(
            resolved, hosts, hostName: namespaceEntry.Name, fileLevel: true);
        var mainRoutineSections = mainRoutineEntry.Section is null
            ? new List<SectionRule> { new(SectionContent.Declarations, DelimiterPair.None, SectionCardinality.ZeroOrMany) }
            : BuildLevel(resolved, hosts, hostName: mainRoutineEntry.Name, fileLevel: false);

        if (mainRoutineEntry.Section is not null &&
            !mainRoutineSections.Any(static rule =>
                rule.Content is SectionContent.SubRoutine or SectionContent.Statements))
        {
            throw Invalid(mainRoutineEntry.Name,
                "must contain either a subroutine section or a statements section — a main routine needs a body.");
        }

        var @namespace = namespaceEntry.Section is null
            ? null
            : BuildRule(namespaceEntry.Name, namespaceEntry.Section, SectionContent.Namespace, SectionCardinality.OneOrMany);

        return new SectionPlan(@namespace, fileSections, mainRoutineSections);
    }

    /// <summary>
    /// Maps each hosted section name to its host; validates hosting and reachability.
    /// Declarations-kind sections are the one multi-host exception: they hoist into
    /// the single file-scope list wherever they appear, so several hosts may list
    /// them (CL/CLX allow declaration blocks at file level and between sub-routines alike).
    /// </summary>
    private static Dictionary<string, string> BuildHostIndex(
        List<(string Name, RecipeSection Section, SectionContent Content)> resolved,
        string? namespaceName,
        string? mainRoutineName)
    {
        var hosts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (hostName, host, _) in resolved)
        {
            foreach (var child in host.MustContain.Concat(host.CanContain))
            {
                var childEntry = resolved.FirstOrDefault(r => string.Equals(r.Name, child, StringComparison.OrdinalIgnoreCase));
                if (childEntry.Section is null)
                {
                    throw Invalid(hostName, $"contains '{child}', which is not a section of this recipe.");
                }

                if (string.Equals(child, hostName, StringComparison.OrdinalIgnoreCase))
                {
                    throw Invalid(hostName, "a section cannot contain itself.");
                }

                if (hosts.TryGetValue(child, out var existing) &&
                    childEntry.Content != SectionContent.Declarations)
                {
                    throw Invalid(child,
                        $"is contained by both '{existing}' and '{hostName}'; only declarations may live at several levels.");
                }

                hosts[child] = hostName;
            }

            if (host.MustContain.Intersect(host.CanContain, StringComparer.OrdinalIgnoreCase).Any())
            {
                throw Invalid(hostName, "lists the same section under both mustContain and canContain.");
            }
        }

        foreach (var (name, _, content) in resolved)
        {
            if (content == SectionContent.Namespace)
            {
                if (hosts.ContainsKey(name))
                {
                    throw Invalid(name, "the namespace section cannot be contained by another section.");
                }

                continue;
            }

            if (namespaceName is not null && !hosts.ContainsKey(name))
            {
                throw Invalid(name,
                    $"is not contained by any section; with a namespace ('{namespaceName}') present, every " +
                    "other section must be listed in a mustContain/canContain chain from it.");
            }

            if (content == SectionContent.SubRoutine && mainRoutineName is not null &&
                hosts.TryGetValue(name, out var host) &&
                !string.Equals(host, mainRoutineName, StringComparison.OrdinalIgnoreCase))
            {
                throw Invalid(name,
                    $"a subroutine section must live inside the mainroutine section ('{mainRoutineName}') when one exists.");
            }
        }

        return hosts;
    }

    private static List<SectionRule> BuildLevel(
        List<(string Name, RecipeSection Section, SectionContent Content)> resolved,
        Dictionary<string, string> hosts,
        string? hostName,
        bool fileLevel)
    {
        var host = hostName is null
            ? null
            : resolved.First(r => string.Equals(r.Name, hostName, StringComparison.OrdinalIgnoreCase)).Section;

        var rules = new List<SectionRule>();
        foreach (var (name, section, content) in resolved)
        {
            if (content == SectionContent.Namespace)
            {
                continue;
            }

            bool atThisLevel;
            bool required;
            if (host is not null)
            {
                var inMust = host.MustContain.Contains(name, StringComparer.OrdinalIgnoreCase);
                var inCan = host.CanContain.Contains(name, StringComparer.OrdinalIgnoreCase);
                atThisLevel = inMust || inCan;
                required = inMust;
            }
            else
            {
                // No host section at this level: file level holds the uncontained sections.
                atThisLevel = fileLevel && !hosts.ContainsKey(name);
                required = false;
            }

            if (!atThisLevel)
            {
                continue;
            }

            required = ReconcilePresence(name, section, required, hosted: host is not null);
            rules.Add(BuildRule(name, section, content,
                required ? SectionCardinality.OneOrMany : SectionCardinality.ZeroOrMany));
        }

        if (rules.Count(static r => r.Content is SectionContent.SubRoutine or SectionContent.Statements) > 1 &&
            rules.Any(static r => r.Content == SectionContent.Statements))
        {
            throw Invalid(hostName ?? "file",
                "cannot contain both a subroutine section and a statements section — bare statements are " +
                "only meaningful where no routine section exists.");
        }

        // Statements matches anything, so it must be tried last.
        rules = rules.OrderBy(static r => r.Content == SectionContent.Statements ? 1 : 0).ToList();
        return rules;
    }

    /// <summary>
    /// A hosted section's requiredness comes from its host's lists; the section's own
    /// <c>presence</c> may restate it but not contradict it. File-level (uncontained)
    /// sections have only <c>presence</c>.
    /// </summary>
    private static bool ReconcilePresence(string name, RecipeSection section, bool requiredByHost, bool hosted)
    {
        var presence = section.Presence?.Trim().ToLowerInvariant();
        var declaredRequired = presence switch
        {
            null or "" => (bool?)null,
            "required" => true,
            "optional" => false,
            _ => throw Invalid(name, $"presence is '{section.Presence}' — expected 'required' or 'optional'."),
        };

        if (!hosted)
        {
            return declaredRequired ?? false;
        }

        if (declaredRequired is { } declared && declared != requiredByHost)
        {
            throw Invalid(name, requiredByHost
                ? "presence says 'optional' but the host lists it under mustContain — move it to canContain or drop the presence field."
                : "presence says 'required' but the host lists it under canContain — move it to mustContain or drop the presence field.");
        }

        return requiredByHost;
    }

    private static SectionRule BuildRule(string name, RecipeSection section, SectionContent content, SectionCardinality cardinality)
    {
        var keywordMode = section.Start is not null;
        var patternMode = section.Format is not null;
        if (keywordMode && patternMode)
        {
            throw Invalid(name, "declares both 'start' (keyword shorthand) and 'format' (pattern) — pick one spelling.");
        }

        if (section.End is not null && section.EndFormat is not null)
        {
            throw Invalid(name, "declares both 'end' (keyword) and 'endFormat' (pattern) — pick one spelling.");
        }

        // Checked before the header-spelling requirement so a section that wrote
        // only an 'end' gets the message about its actual mistake.
        if (section.End is not null && !keywordMode)
        {
            throw Invalid(name, "'end' keyword shorthand requires 'start'; with 'format' use 'endFormat'.");
        }

        if (!keywordMode && !patternMode &&
            content is not (SectionContent.Declarations or SectionContent.Statements))
        {
            throw Invalid(name, $"a '{content}' section needs a header spelling: either 'start' (keyword) or 'format' (pattern).");
        }

        var hasTerminator = section.End is not null || section.EndFormat is not null;
        if (section.EndOptional && !hasTerminator)
        {
            throw Invalid(name, "'endOptional' without an 'end'/'endFormat' terminator has nothing to make optional.");
        }

        if (section.EndEmit is not null && !hasTerminator)
        {
            throw Invalid(name, "'endEmit' without an 'end'/'endFormat' terminator has nothing to emit.");
        }

        var startFirstWord = patternMode ? LeadingWord(section.Format!) : null;
        if (patternMode && startFirstWord is null)
        {
            throw Invalid(name, "'format' must begin with a literal keyword (an identifier), so the parser can dispatch on it.");
        }

        string? endFirstWord = null;
        if (section.EndFormat is not null)
        {
            endFirstWord = LeadingWord(section.EndFormat);
            if (endFirstWord is null)
            {
                throw Invalid(name, "'endFormat' must begin with a literal keyword (an identifier).");
            }
        }

        if (section.Defaults.Count > 0 && !patternMode)
        {
            throw Invalid(name, "'defaults' fill format-pattern placeholders — this section has no 'format'.");
        }

        return new SectionRule(
            content,
            keywordMode ? new DelimiterPair(section.Start, section.End).Normalized() : DelimiterPair.None,
            cardinality)
        {
            Name = name,
            StartPattern = section.Format,
            EmitTemplate = patternMode ? section.Emit ?? StripCaptureTypes(section.Format!) : null,
            EndPattern = section.EndFormat,
            EndEmitTemplate = section.EndEmit,
            EndOptional = section.EndOptional,
            Defaults = section.Defaults,
            StartFirstWord = startFirstWord,
            EndFirstWord = endFirstWord,
        };
    }

    /// <summary>The leading literal identifier of a pattern ("SEQUENCE {name}…" → "SEQUENCE"); null when it has none.</summary>
    public static string? LeadingWord(string pattern)
    {
        var trimmed = pattern.TrimStart();
        var length = 0;
        while (length < trimmed.Length && (char.IsLetterOrDigit(trimmed[length]) || trimmed[length] == '_'))
        {
            length++;
        }

        return length > 0 && (char.IsLetter(trimmed[0]) || trimmed[0] == '_') ? trimmed[..length] : null;
    }

    /// <summary>"{name:identifier}" → "{name}": the default emit template of a format pattern.</summary>
    public static string StripCaptureTypes(string pattern) =>
        System.Text.RegularExpressions.Regex.Replace(pattern, @"\{(\w+):\w+\}", "{$1}");

    private static InvalidOperationException Invalid(string section, string message) =>
        new($"recipe.{section}: {message}");
}
