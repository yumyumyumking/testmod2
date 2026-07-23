namespace Transpiler.Engine.Transform.Lowering;

/// <summary>
/// Tier-2 lowering (SPEC §7.2): replaces rule-owned constructs with vendor marker
/// lines. TRY/CATCH becomes a begin/middle/end frame; array declarations and pure
/// index load/store statements become single marker lines. Runs before structural
/// lowering so frame bodies still contain the structured constructs for it to lower.
/// </summary>
public sealed class MappingLowerPass : IAstPass
{
    public string Name => "mapping-lower";

    public ProgramSyntax Run(ProgramSyntax program, PassContext context) =>
        program.MapSubRoutines(subRoutine => subRoutine.WithBody(LowerList(subRoutine.Body, context)));

    private List<Statement> LowerList(IReadOnlyList<Statement> statements, PassContext context)
    {
        var output = new List<Statement>();

        foreach (var statement in statements)
        {
            switch (statement)
            {
                case TryStatement t:
                    LowerTry(t, context, output);
                    break;

                case ArrayDeclarationStatement array:
                {
                    var mapping = context.Rules.Find(MappingSelectors.ArrayDeclaration);
                    if (mapping is null)
                    {
                        context.Diagnostics.Report(DiagnosticCodes.CapabilityMissing, array.Span, "ARRAY", MappingSelectors.ArrayDeclaration);
                        output.Add(array);
                        break;
                    }

                    output.Add(MakeMarker(mapping, MarkerRole.Statement, new Dictionary<string, string>
                    {
                        ["name"] = array.Name,
                        ["size"] = array.Size,
                    }, mapping.Mapping.Lower.Format!, array));
                    break;
                }

                case AssignmentStatement { Target: IndexReference store } assign:
                {
                    var mapping = context.Rules.Find(MappingSelectors.IndexedStore);
                    if (mapping is null)
                    {
                        context.Diagnostics.Report(DiagnosticCodes.CapabilityMissing, assign.Span, "indexed store", MappingSelectors.IndexedStore);
                        output.Add(assign);
                        break;
                    }

                    if (ExpressionOps.ContainsIndexReference(assign.Value))
                    {
                        context.Diagnostics.Report(DiagnosticCodes.IndexedAccessUnsupported, assign.Span);
                        output.Add(assign);
                        break;
                    }

                    output.Add(MakeMarker(mapping, MarkerRole.Statement, new Dictionary<string, string>
                    {
                        ["array"] = store.Name,
                        ["index"] = ExpressionWriter.Write(store.Index, context.TargetLanguage),
                        ["value"] = ExpressionWriter.Write(assign.Value, context.TargetLanguage),
                    }, mapping.Mapping.Lower.Format!, assign));
                    break;
                }

                case AssignmentStatement { Target: NameReference dest, Value: IndexReference load } assign:
                {
                    var mapping = context.Rules.Find(MappingSelectors.IndexedLoad);
                    if (mapping is null)
                    {
                        context.Diagnostics.Report(DiagnosticCodes.CapabilityMissing, assign.Span, "indexed load", MappingSelectors.IndexedLoad);
                        output.Add(assign);
                        break;
                    }

                    output.Add(MakeMarker(mapping, MarkerRole.Statement, new Dictionary<string, string>
                    {
                        ["dest"] = dest.Name,
                        ["array"] = load.Name,
                        ["index"] = ExpressionWriter.Write(load.Index, context.TargetLanguage),
                    }, mapping.Mapping.Lower.Format!, assign));
                    break;
                }

                case AssignmentStatement assign when ExpressionOps.ContainsIndexReference(assign.Value):
                    context.Diagnostics.Report(DiagnosticCodes.IndexedAccessUnsupported, assign.Span);
                    output.Add(assign);
                    break;

                // Indexed access anywhere other than a whole-statement load/store has
                // no marker form in the flat target — diagnose instead of emitting
                // CL that references an array the target cannot declare.
                case SetStatement set when ExpressionOps.ContainsIndexReference(set.Target):
                    context.Diagnostics.Report(DiagnosticCodes.IndexedAccessUnsupported, set.Span);
                    output.Add(set);
                    break;

                case IfBlockStatement i:
                {
                    foreach (var branch in i.Branches)
                    {
                        if (ExpressionOps.ContainsIndexReference(branch.Condition))
                        {
                            context.Diagnostics.Report(DiagnosticCodes.IndexedAccessUnsupported, i.Span);
                        }
                    }

                    var branches = i.Branches
                        .Select(b => new IfBranch(b.Condition, LowerList(b.Body, context)))
                        .ToList();
                    var elseBody = i.ElseBody is null ? null : LowerList(i.ElseBody, context);
                    output.Add(new IfBlockStatement(branches, elseBody)
                    {
                        Span = i.Span,
                        LeadingComments = i.LeadingComments,
                        TrailingComment = i.TrailingComment,
                    });
                    break;
                }

                case WhileStatement w:
                    if (ExpressionOps.ContainsIndexReference(w.Condition))
                    {
                        context.Diagnostics.Report(DiagnosticCodes.IndexedAccessUnsupported, w.Span);
                    }

                    output.Add(new WhileStatement(w.Condition, LowerList(w.Body, context))
                    {
                        Span = w.Span,
                        LeadingComments = w.LeadingComments,
                        TrailingComment = w.TrailingComment,
                    });
                    break;

                case RepeatStatement r:
                    if (ExpressionOps.ContainsIndexReference(r.Count))
                    {
                        context.Diagnostics.Report(DiagnosticCodes.IndexedAccessUnsupported, r.Span);
                    }

                    output.Add(new RepeatStatement(r.Count, LowerList(r.Body, context))
                    {
                        Span = r.Span,
                        LeadingComments = r.LeadingComments,
                        TrailingComment = r.TrailingComment,
                    });
                    break;

                default:
                    output.Add(statement);
                    break;
            }
        }

        return output;
    }

    private void LowerTry(TryStatement t, PassContext context, List<Statement> output)
    {
        var mapping = context.Rules.Find(MappingSelectors.TryBlock);
        if (mapping is null)
        {
            context.Diagnostics.Report(DiagnosticCodes.CapabilityMissing, t.Span, "TRY/CATCH", MappingSelectors.TryBlock);
            output.Add(t);
            return;
        }

        var captures = new Dictionary<string, string> { ["faultVar"] = t.FaultVariable };
        var lower = mapping.Mapping.Lower;

        output.Add(MakeMarker(mapping, MarkerRole.Begin, captures, lower.Begin!, t));
        output.AddRange(LowerList(t.Body, context));
        output.Add(MakeMarker(mapping, MarkerRole.Middle, captures, lower.Middle!, t));
        output.AddRange(LowerList(t.Handler, context));
        output.Add(MakeMarker(mapping, MarkerRole.End, captures, lower.End!, t));
    }

    private static MarkerStatement MakeMarker(
        CompiledMapping mapping,
        MarkerRole role,
        IReadOnlyDictionary<string, string> captures,
        string template,
        Statement origin)
    {
        return new MarkerStatement(
            mapping.Rule.Name,
            mapping.MappingId,
            role,
            captures,
            MarkerTemplate.Render(template, captures))
        {
            Span = origin.Span,
            LeadingComments = role == MarkerRole.Begin || role == MarkerRole.Statement
                ? origin.LeadingComments
                : Array.Empty<string>(),
            TrailingComment = role == MarkerRole.Statement ? origin.TrailingComment : null,
        };
    }
}
