using System.Collections;
using System.Globalization;

namespace Transpiler.Core.Diagnostics;

/// <summary>
/// Collects diagnostics for one document, resolving spans to line/column eagerly.
/// </summary>
public sealed class DiagnosticBag : IEnumerable<Diagnostic>
{
    private readonly List<Diagnostic> _items = new();
    private readonly SourceText? _text;

    public DiagnosticBag(SourceText? text = null)
    {
        _text = text;
    }

    public int Count => _items.Count;

    public bool HasErrors => _items.Any(static d => d.Severity == DiagnosticSeverity.Error);

    public void Report(DiagnosticCode code, TextSpan? span, params object[] args)
    {
        var message = args.Length == 0
            ? code.Template
            : string.Format(CultureInfo.InvariantCulture, code.Template, args);

        LinePosition? position = span is { } s && _text is not null
            ? _text.GetLinePosition(s.Start)
            : null;

        _items.Add(new Diagnostic(code, message, span, position));
    }

    public void Add(Diagnostic diagnostic) => _items.Add(diagnostic);

    public void AddRange(IEnumerable<Diagnostic> diagnostics) => _items.AddRange(diagnostics);

    public IReadOnlyList<Diagnostic> ToList() => _items.ToList();

    public IEnumerator<Diagnostic> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
