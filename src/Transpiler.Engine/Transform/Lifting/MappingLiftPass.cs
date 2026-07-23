namespace Transpiler.Engine.Transform.Lifting;

/// <summary>
/// Tier-2 lifting (SPEC §7.2/§8.3): reassembles marker lines recognized by the CL
/// parser into structured nodes. Frame markers (begin/middle/end) become TRY/CATCH
/// with nesting balanced per rule; statement markers become array declarations and
/// index load/store assignments (capture expressions are sub-parsed).
/// </summary>
public sealed class MappingLiftPass : IAstPass
{
    public string Name => "mapping-lift";

    public ProgramSyntax Run(ProgramSyntax program, PassContext context) =>
        program.MapSubRoutines(subRoutine => subRoutine.WithBody(Assemble(subRoutine.Body, context)));

    private List<Statement> Assemble(IReadOnlyList<Statement> statements, PassContext context)
    {
        var output = new List<Statement>();
        var index = 0;

        while (index < statements.Count)
        {
            var statement = statements[index];

            if (statement is MarkerStatement marker)
            {
                switch (marker.Role)
                {
                    case MarkerRole.Begin:
                        if (TryAssembleFrame(statements, ref index, marker, context, out var frame))
                        {
                            output.Add(frame);
                            continue;
                        }

                        context.Diagnostics.Report(DiagnosticCodes.ConfigInvalid, marker.Span,
                            $"unmatched '{marker.Text}' frame marker (rule {marker.RuleName}).");
                        output.Add(marker);
                        index++;
                        continue;

                    case MarkerRole.Statement:
                        output.Add(LiftStatementMarker(marker, context));
                        index++;
                        continue;

                    default:
                        context.Diagnostics.Report(DiagnosticCodes.ConfigInvalid, marker.Span,
                            $"stray '{marker.Text}' marker without a matching frame start (rule {marker.RuleName}).");
                        output.Add(marker);
                        index++;
                        continue;
                }
            }

            output.Add(statement);
            index++;
        }

        return output;
    }

    private bool TryAssembleFrame(
        IReadOnlyList<Statement> statements,
        ref int index,
        MarkerStatement begin,
        PassContext context,
        out Statement frame)
    {
        frame = begin;

        var middleIndex = -1;
        var endIndex = -1;
        var depth = 1;

        for (var i = index + 1; i < statements.Count; i++)
        {
            if (statements[i] is not MarkerStatement m || m.RuleName != begin.RuleName || m.MappingId != begin.MappingId)
            {
                continue;
            }

            switch (m.Role)
            {
                case MarkerRole.Begin:
                    depth++;
                    break;
                case MarkerRole.Middle when depth == 1 && middleIndex < 0:
                    middleIndex = i;
                    break;
                case MarkerRole.End:
                    depth--;
                    if (depth == 0)
                    {
                        endIndex = i;
                    }

                    break;
            }

            if (endIndex >= 0)
            {
                break;
            }
        }

        if (endIndex < 0 || middleIndex < 0)
        {
            return false;
        }

        var selector = context.Rules.SelectorFor(begin.RuleName, begin.MappingId);
        if (selector != MappingSelectors.TryBlock)
        {
            return false;
        }

        var middle = (MarkerStatement)statements[middleIndex];
        var body = Assemble(Slice(statements, index + 1, middleIndex), context);
        var handler = Assemble(Slice(statements, middleIndex + 1, endIndex), context);
        var faultVariable = middle.Captures.TryGetValue("faultVar", out var fault) ? fault : "fault";

        frame = new TryStatement(faultVariable, body, handler)
        {
            Span = begin.Span,
            LeadingComments = begin.LeadingComments,
        };
        index = endIndex + 1;
        return true;
    }

    private Statement LiftStatementMarker(MarkerStatement marker, PassContext context)
    {
        var selector = context.Rules.SelectorFor(marker.RuleName, marker.MappingId);
        switch (selector)
        {
            case MappingSelectors.ArrayDeclaration:
                return new ArrayDeclarationStatement(Capture(marker, "name"), Capture(marker, "size"))
                {
                    Span = marker.Span,
                    LeadingComments = marker.LeadingComments,
                    TrailingComment = marker.TrailingComment,
                };

            case MappingSelectors.IndexedStore:
            {
                var target = new IndexReference(Capture(marker, "array"), ParseCaptureExpression(marker, "index", context));
                var value = ParseCaptureExpression(marker, "value", context);
                return new AssignmentStatement(target, value)
                {
                    Span = marker.Span,
                    LeadingComments = marker.LeadingComments,
                    TrailingComment = marker.TrailingComment,
                };
            }

            case MappingSelectors.IndexedLoad:
            {
                var value = new IndexReference(Capture(marker, "array"), ParseCaptureExpression(marker, "index", context));
                return new AssignmentStatement(new NameReference(Capture(marker, "dest")), value)
                {
                    Span = marker.Span,
                    LeadingComments = marker.LeadingComments,
                    TrailingComment = marker.TrailingComment,
                };
            }

            default:
                context.Diagnostics.Report(DiagnosticCodes.ConfigInvalid, marker.Span,
                    $"marker '{marker.Text}' has no liftable selector (rule {marker.RuleName}).");
                return marker;
        }
    }

    private static string Capture(MarkerStatement marker, string name) =>
        marker.Captures.TryGetValue(name, out var value) ? value : string.Empty;

    private static Expression ParseCaptureExpression(MarkerStatement marker, string name, PassContext context)
    {
        var text = Capture(marker, name);
        var bag = new DiagnosticBag();
        var expression = Parser.ParseExpressionText(text, context.TargetLanguage, bag);
        if (bag.HasErrors)
        {
            context.Diagnostics.Report(DiagnosticCodes.ConfigInvalid, marker.Span,
                $"capture '{name}' of marker '{marker.Text}' is not a valid expression: '{text}'.");
        }

        return expression;
    }

    private static List<Statement> Slice(IReadOnlyList<Statement> source, int start, int endExclusive)
    {
        var list = new List<Statement>(Math.Max(0, endExclusive - start));
        for (var i = start; i < endExclusive; i++)
        {
            list.Add(source[i]);
        }

        return list;
    }
}
