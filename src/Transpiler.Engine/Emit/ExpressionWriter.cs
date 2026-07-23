using System.Text;

namespace Transpiler.Engine.Emit;

/// <summary>
/// Renders expressions with language keyword spellings and minimal, precedence-correct
/// parenthesisation. Rendering is deterministic: parse(render(e)) == e structurally.
/// </summary>
public static class ExpressionWriter
{
    public static string Write(Expression expression, LanguageProfile language)
    {
        var sb = new StringBuilder();
        WriteExpression(sb, expression, language, parentPrecedence: 0);
        return sb.ToString();
    }

    private static int Precedence(Expression e) => e switch
    {
        BinaryExpression b => b.Operator switch
        {
            BinaryOperator.Or => 1,
            BinaryOperator.And => 2,
            BinaryOperator.Equal or BinaryOperator.NotEqual or BinaryOperator.Less or
            BinaryOperator.LessOrEqual or BinaryOperator.Greater or BinaryOperator.GreaterOrEqual => 4,
            BinaryOperator.Add or BinaryOperator.Subtract => 5,
            _ => 6,
        },
        UnaryExpression u => u.Operator == UnaryOperator.Not ? 3 : 7,
        _ => 8,
    };

    private static void WriteExpression(StringBuilder sb, Expression e, LanguageProfile language, int parentPrecedence)
    {
        var precedence = Precedence(e);
        var needParens = precedence < parentPrecedence;
        if (needParens)
        {
            sb.Append('(');
        }

        switch (e)
        {
            case NumberLiteral n:
                sb.Append(n.Text);
                break;
            case StringLiteral s:
                sb.Append(s.RawText);
                break;
            case BoolLiteral b:
                sb.Append(b.Value ? language.Keywords.BoolTrue : language.Keywords.BoolFalse);
                break;
            case NameReference name:
                sb.Append(name.Name);
                break;
            case IndexReference index:
                sb.Append(index.Name).Append('[');
                WriteExpression(sb, index.Index, language, 0);
                sb.Append(']');
                break;
            case UnaryExpression u when u.Operator == UnaryOperator.Not:
                sb.Append(language.Keywords.Not).Append(' ');
                WriteExpression(sb, u.Operand, language, precedence + 1);
                break;
            case UnaryExpression u:
                sb.Append('-');
                WriteExpression(sb, u.Operand, language, precedence + 1);
                break;
            case BinaryExpression bin:
            {
                // Left operand at own precedence (left-associative), right one tighter.
                WriteExpression(sb, bin.Left, language, precedence);
                sb.Append(' ').Append(OperatorText(bin.Operator, language)).Append(' ');
                WriteExpression(sb, bin.Right, language, precedence + 1);
                break;
            }
        }

        if (needParens)
        {
            sb.Append(')');
        }
    }

    private static string OperatorText(BinaryOperator op, LanguageProfile language) => op switch
    {
        BinaryOperator.Or => language.Keywords.Or,
        BinaryOperator.And => language.Keywords.And,
        BinaryOperator.Equal => language.Keywords.Equal,
        BinaryOperator.NotEqual => language.Keywords.NotEqual,
        BinaryOperator.Less => "<",
        BinaryOperator.LessOrEqual => "<=",
        BinaryOperator.Greater => ">",
        BinaryOperator.GreaterOrEqual => ">=",
        BinaryOperator.Add => "+",
        BinaryOperator.Subtract => "-",
        BinaryOperator.Multiply => "*",
        _ => "/",
    };
}
