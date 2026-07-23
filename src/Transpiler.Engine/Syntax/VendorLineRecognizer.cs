using System.Text.RegularExpressions;

namespace Transpiler.Engine.Syntax;

/// <summary>
/// Recognition of tier-2 vendor marker lines: tries every mapping rule's compiled
/// lift pattern against an otherwise-unrecognized statement line and produces the
/// <see cref="MarkerStatement"/> the lift pass will later assemble. Isolated from
/// <see cref="Parser"/> so mapping-rule knowledge stays out of the grammar core.
/// A pathological pattern (regex timeout) degrades to a configuration diagnostic
/// and the pattern is skipped — never an exception out of parsing.
/// </summary>
public sealed class VendorLineRecognizer
{
    private readonly IReadOnlyList<VendorPattern> _patterns;

    public VendorLineRecognizer(IReadOnlyList<VendorPattern> patterns)
    {
        _patterns = patterns ?? Array.Empty<VendorPattern>();
    }

    /// <summary>First matching pattern wins (patterns arrive priority-ordered); null when no pattern matches.</summary>
    public MarkerStatement? TryMatch(string rawLine, TextSpan span, DiagnosticBag diagnostics)
    {
        foreach (var pattern in _patterns)
        {
            Match match;
            try
            {
                match = pattern.Regex.Match(rawLine);
            }
            catch (RegexMatchTimeoutException)
            {
                diagnostics.Report(DiagnosticCodes.ConfigInvalid, span,
                    $"lift pattern of rule '{pattern.RuleName}' timed out matching this line; pattern skipped.");
                continue;
            }

            if (!match.Success)
            {
                continue;
            }

            var captures = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var groupName in pattern.Regex.GetGroupNames())
            {
                if (!int.TryParse(groupName, out _))
                {
                    captures[groupName] = match.Groups[groupName].Value;
                }
            }

            return new MarkerStatement(pattern.RuleName, pattern.MappingId, pattern.Role, captures, rawLine);
        }

        return null;
    }
}
