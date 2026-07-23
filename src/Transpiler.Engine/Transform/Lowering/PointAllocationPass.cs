namespace Transpiler.Engine.Transform.Lowering;

/// <summary>
/// Point allocation for targets whose local variables must be bound to points
/// (<c>capabilities.pointAllocation</c>): unbound locals get slots allocated
/// deterministically (declaration order, ascending indexes, skipping explicitly
/// bound points, up to <c>localCapacity</c> per point type) in the target's point
/// area. Everything here is configuration, not code: the point type per variable
/// kind comes from the target's <c>variables.kinds</c>, and the area's name from
/// <c>capabilities.allocationField</c> — the header field that carries it, defaulted
/// through the namespace section's <c>defaults</c> exactly as emission fills the
/// header, so bindings and the emitted header always agree. Exceeding capacity is
/// CLX2401; two declarations claiming one point is CLX2402; a local of a kind the
/// target declares no point type for is CLX2302.
/// </summary>
public sealed class PointAllocationPass : IAstPass
{
    public string Name => "point-allocation";

    public ProgramSyntax Run(ProgramSyntax program, PassContext context)
    {
        var variables = context.TargetLanguage.Variables;
        var capacity = context.TargetLanguage.Capabilities.LocalCapacity;
        var comparer = context.TargetLanguage.NameComparer;
        var area = ResolveArea(program, context.TargetLanguage);

        // One index pool per configured point type.
        var pointTypes = variables.Kinds
            .Where(static rule => rule.Point is not null)
            .Select(static rule => rule.Point!)
            .ToList();
        var used = pointTypes.ToDictionary(static p => p, static _ => new HashSet<int>(), comparer);
        var nextIndex = pointTypes.ToDictionary(static p => p, static _ => 1, comparer);
        var allocatedCount = pointTypes.ToDictionary(static p => p, static _ => 0, comparer);

        // Indexes already taken by explicit bindings to the target area.
        foreach (var declaration in program.Declarations)
        {
            if (declaration.Binding is not { } binding ||
                !comparer.Equals(binding.Area, area) ||
                !used.TryGetValue(binding.PointType, out var indexes) ||
                !int.TryParse(binding.Index, out var index))
            {
                continue;
            }

            if (!indexes.Add(index))
            {
                context.Diagnostics.Report(DiagnosticCodes.DuplicatePointBinding, declaration.Span,
                    binding.Area, binding.PointType, binding.Index);
            }
        }

        var declarations = new List<VariableDeclaration>(program.Declarations.Count);
        foreach (var declaration in program.Declarations)
        {
            if (declaration.Scope != VariableScopeKind.Local || declaration.Binding is not null)
            {
                declarations.Add(declaration);
                continue;
            }

            var kindRule = variables.KindRuleFor(declaration.EffectiveKind);
            if (kindRule?.Point is not { } pointType)
            {
                context.Diagnostics.Report(DiagnosticCodes.CapabilityMissing, declaration.Span,
                    $"local '{declaration.Name}' of kind {declaration.EffectiveKind}",
                    $"variables.kinds ({declaration.EffectiveKind} with a 'point' type)");
                declarations.Add(declaration);
                continue;
            }

            var indexes = used[pointType];
            var index = nextIndex[pointType];
            while (index <= capacity && indexes.Contains(index))
            {
                index++;
            }

            if (index > capacity)
            {
                context.Diagnostics.Report(DiagnosticCodes.PointCapacityExceeded, declaration.Span,
                    area, pointType, allocatedCount[pointType] + 1, capacity);
                declarations.Add(declaration);
                continue;
            }

            indexes.Add(index);
            nextIndex[pointType] = index + 1;
            allocatedCount[pointType]++;

            declarations.Add(new VariableDeclaration(
                declaration.Scope,
                declaration.Name,
                declaration.EffectiveKind,
                new PointBinding(area, pointType, index.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                declaration.IsGenerated)
            {
                Span = declaration.Span,
                LeadingComments = declaration.LeadingComments,
                TrailingComment = declaration.TrailingComment,
            });
        }

        return program.WithDeclarations(declarations);
    }

    /// <summary>
    /// The point area allocations go into: the configured header field when the
    /// program's header carries it, else the namespace section's default for that
    /// field (loader validation guarantees one exists), else the synthesized-header
    /// name as a last resort for hand-built, unvalidated profiles.
    /// </summary>
    private static string ResolveArea(ProgramSyntax program, LanguageProfile target)
    {
        var field = target.Capabilities.AllocationField;
        if (string.IsNullOrWhiteSpace(field))
        {
            return ProgramHeader.DefaultName;
        }

        if (program.Header is { } header && header.Fields.TryGetValue(field, out var fromHeader))
        {
            return fromHeader;
        }

        return target.Plan.Namespace is { } ns && ns.Defaults.TryGetValue(field, out var fromDefaults)
            ? fromDefaults
            : ProgramHeader.DefaultName;
    }
}
