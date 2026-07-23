using System.Text.Json.Serialization;

namespace Transpiler.Core.Languages;

/// <summary>
/// The delimiters of one section kind. Both parts are optional, and the two nulls
/// mean different things:
/// <list type="bullet">
/// <item><see cref="Start"/> is <c>null</c> — the language does not have this section
/// at all. The parser never looks for it; content that would live inside it sits one
/// level up (e.g. no MainRoutine section → routines at file level are wrapped in one
/// implicit main routine).</item>
/// <item><see cref="End"/> is <c>null</c> — the section has no terminator; it runs
/// until the next section start or end of file (how PHASE already behaves in CL).</item>
/// </list>
/// </summary>
public sealed record DelimiterPair(string? Start, string? End)
{
    /// <summary>The "section does not exist" pair.</summary>
    public static DelimiterPair None { get; } = new(null, null);

    /// <summary>True when the language has this section.</summary>
    [JsonIgnore]
    public bool Exists => Start is not null;

    /// <summary>True when the section is closed by an explicit end keyword.</summary>
    [JsonIgnore]
    public bool HasTerminator => End is not null;

    /// <summary>Trims both parts; whitespace-only spellings become null.</summary>
    public DelimiterPair Normalized()
    {
        var start = Clean(Start);
        var end = Clean(End);
        return start == Start && end == End ? this : new DelimiterPair(start, end);
    }

    private static string? Clean(string? value)
    {
        if (value is null)
        {
            return null;
        }

        value = value.Trim();
        return value.Length == 0 ? null : value;
    }
}
