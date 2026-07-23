namespace Transpiler.Core.Syntax;

public sealed class TryStatement : Statement
{
    public TryStatement(string faultVariable, IReadOnlyList<Statement> body, IReadOnlyList<Statement> handler)
    {
        FaultVariable = faultVariable;
        Body = body;
        Handler = handler;
    }

    public string FaultVariable { get; }

    public IReadOnlyList<Statement> Body { get; }

    public IReadOnlyList<Statement> Handler { get; }
}
