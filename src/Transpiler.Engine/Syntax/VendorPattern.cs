using System.Text.RegularExpressions;

namespace Transpiler.Engine.Syntax;

/// <summary>
/// A compiled recognizer for one vendor line shape contributed by a tier-2 mapping
/// rule (SPEC §7.2). The CL parser tries these against otherwise-unrecognized lines
/// and produces <see cref="MarkerStatement"/>s on match.
/// </summary>
public sealed class VendorPattern
{
    public VendorPattern(string ruleName, string mappingId, MarkerRole role, Regex regex)
    {
        RuleName = ruleName;
        MappingId = mappingId;
        Role = role;
        Regex = regex;
    }

    public string RuleName { get; }

    public string MappingId { get; }

    public MarkerRole Role { get; }

    public Regex Regex { get; }
}
