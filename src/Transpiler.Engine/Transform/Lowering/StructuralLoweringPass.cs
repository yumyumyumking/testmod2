namespace Transpiler.Engine.Transform.Lowering;

/// <summary>
/// Lowers structured control flow onto CL's flat substrate (SPEC §8.2): block
/// IF/ELSIF/ELSE, WHILE and REPEAT become conditional GOTOs with deterministically
/// named labels. Trivial conditionals keep CL's native single-line IF form so
/// idiomatic input stays idiomatic. Label allocation is per routine and stable,
/// which makes re-lowering byte-identical and the round trip verifiable.
/// </summary>
public sealed class StructuralLoweringPass : IAstPass
{
    public string Name => "structural-lower";

    public ProgramSyntax Run(ProgramSyntax program, PassContext context)
    {
        // Counter declarations injected by REPEAT lowering are pass-local state,
        // appended to the header at the end of this pass's own run.
        var generated = new List<VariableDeclaration>();

        // Labels are scoped per STEP, so numbering restarts per subRoutine.
        var result = program.MapSubRoutines(subRoutine =>
        {
            context.Labels.ResetRoutine();
            return subRoutine.WithBody(LowerList(subRoutine.Body, context, generated));
        });

        var declarations = result.Declarations.Concat(generated).ToList();
        return result.WithDeclarations(declarations);
    }

    private List<Statement> LowerList(IReadOnlyList<Statement> statements, PassContext context, List<VariableDeclaration> generated)
    {
        var output = new List<Statement>();

        foreach (var statement in statements)
        {
            switch (statement)
            {
                case IfBlockStatement i:
                    LowerIf(i, context, generated, output);
                    break;
                case WhileStatement w:
                    LowerWhile(w, context, generated, output);
                    break;
                case RepeatStatement r:
                    LowerRepeat(r, context, generated, output);
                    break;
                default:
                    output.Add(statement);
                    break;
            }
        }

        return output;
    }

    private void LowerIf(IfBlockStatement node, PassContext context, List<VariableDeclaration> generated, List<Statement> output)
    {
        // Normalize ELSIF chains into nested IFs so every level gets its own end label
        // (required for unambiguous lifting — SPEC §8.3).
        var normalized = IfChains.Normalize(node);

        if (TryLowerNative(normalized, context, output))
        {
            return;
        }

        LowerIfChain(normalized.Branches, 0, normalized.ElseBody, normalized, context, generated, output);
    }

    private void LowerIfChain(
        IReadOnlyList<IfBranch> branches,
        int index,
        IReadOnlyList<Statement>? elseBody,
        IfBlockStatement origin,
        PassContext context,
        List<VariableDeclaration> generated,
        List<Statement> output)
    {
        var branch = branches[index];
        // The guard emits the INVERTED condition — representability must be judged
        // on what is actually written (NOT x is complex even when x is simple).
        var inverted = ExpressionOps.Invert(branch.Condition);
        CheckConditionRepresentable(inverted, origin, context);

        var body = LowerList(branch.Body, context, generated);
        var hasTail = index + 1 < branches.Count || elseBody is not null;
        var (elseLabel, endLabel) = context.Labels.NextIf();

        // The origin block's comments ride on the first guard so lowering does not
        // silently drop them (mirrors LowerWhile/LowerRepeat).
        var leading = index == 0 ? origin.LeadingComments : Array.Empty<string>();

        if (!hasTail)
        {
            output.Add(Guard(inverted, endLabel, origin, leading));
            output.AddRange(body);
            output.Add(new LabelStatement(endLabel));
            return;
        }

        output.Add(Guard(inverted, elseLabel, origin, leading));
        output.AddRange(body);
        output.Add(new GotoStatement(endLabel));
        output.Add(new LabelStatement(elseLabel));

        if (index + 1 < branches.Count)
        {
            LowerIfChain(branches, index + 1, elseBody, origin, context, generated, output);
        }
        else
        {
            output.AddRange(LowerList(elseBody!, context, generated));
        }

        output.Add(new LabelStatement(endLabel));
    }

    private void LowerWhile(WhileStatement node, PassContext context, List<VariableDeclaration> generated, List<Statement> output)
    {
        var inverted = ExpressionOps.Invert(node.Condition);
        CheckConditionRepresentable(inverted, node, context);
        var (topLabel, endLabel) = context.Labels.NextWhile();
        var body = LowerList(node.Body, context, generated);

        output.Add(new LabelStatement(topLabel) { LeadingComments = node.LeadingComments });
        output.Add(Guard(inverted, endLabel, node));
        output.AddRange(body);
        output.Add(new GotoStatement(topLabel));
        output.Add(new LabelStatement(endLabel));
    }

    private void LowerRepeat(RepeatStatement node, PassContext context, List<VariableDeclaration> generated, List<Statement> output)
    {
        if (!context.TargetLanguage.Capabilities.Arithmetic)
        {
            context.Diagnostics.Report(DiagnosticCodes.CapabilityMissing, node.Span, "REPEAT", "arithmetic");
            output.Add(node);
            return;
        }

        var (topLabel, endLabel, counter) = context.Labels.NextRepeat();
        generated.Add(
            new VariableDeclaration(VariableScopeKind.Local, counter, VariableKind.Numeric, binding: null, isGenerated: true));

        var counterRef = new NameReference(counter);
        var body = LowerList(node.Body, context, generated);

        output.Add(new AssignmentStatement(counterRef, new NumberLiteral("0")) { LeadingComments = node.LeadingComments });
        output.Add(new LabelStatement(topLabel));
        output.Add(Guard(new BinaryExpression(BinaryOperator.GreaterOrEqual, counterRef, node.Count), endLabel, node));
        output.AddRange(body);
        output.Add(new AssignmentStatement(counterRef, new BinaryExpression(BinaryOperator.Add, counterRef, new NumberLiteral("1"))));
        output.Add(new GotoStatement(topLabel));
        output.Add(new LabelStatement(endLabel));
    }

    /// <summary>IF invertedCondition THEN GOTO label — the lowered guard shape.</summary>
    private static IfActionStatement Guard(
        Expression invertedCondition, string label, Statement origin, IReadOnlyList<string>? leadingComments = null) =>
        new(invertedCondition, new GotoStatement(label), elseAction: null)
        {
            Span = origin.Span,
            LeadingComments = leadingComments ?? Array.Empty<string>(),
        };

    /// <summary>
    /// Trivial conditionals lower to CL's native one-line IF: exactly one branch,
    /// single-action bodies, and no vendor/marker or block statements in the actions.
    /// </summary>
    private bool TryLowerNative(IfBlockStatement node, PassContext context, List<Statement> output)
    {
        if (node.Branches.Count != 1)
        {
            return false;
        }

        var branch = node.Branches[0];
        if (branch.Body.Count != 1 || !IsInlineAction(branch.Body[0]))
        {
            return false;
        }

        Statement? elseAction = null;
        if (node.ElseBody is not null)
        {
            if (node.ElseBody.Count != 1 || !IsInlineAction(node.ElseBody[0]))
            {
                return false;
            }

            elseAction = node.ElseBody[0];
        }

        CheckConditionRepresentable(branch.Condition, node, context);
        output.Add(new IfActionStatement(branch.Condition, branch.Body[0], elseAction)
        {
            Span = node.Span,
            LeadingComments = node.LeadingComments,
            TrailingComment = node.TrailingComment,
        });
        return true;
    }

    private static bool IsInlineAction(Statement statement) => statement switch
    {
        SetStatement => true,
        AssignmentStatement a => a.Target is NameReference,
        GotoStatement => true,
        CallStatement => true,
        ReturnStatement => true,
        _ => false,
    };

    private static void CheckConditionRepresentable(Expression condition, Statement origin, PassContext context)
    {
        if (!context.TargetLanguage.Capabilities.ComplexConditions &&
            ExpressionOps.IsComplexCondition(condition))
        {
            context.Diagnostics.Report(DiagnosticCodes.ExpressionNotRepresentable, origin.Span);
        }
    }
}
