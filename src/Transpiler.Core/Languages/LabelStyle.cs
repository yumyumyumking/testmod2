namespace Transpiler.Core.Languages;

/// <summary>Label configuration for a language.</summary>
public sealed class LabelStyle
{
    /// <summary>Suffix that terminates a label definition.</summary>
    public string Suffix { get; init; } = ":";

    /// <summary>Prefix reserved for transpiler-generated labels and temporaries.</summary>
    public string GeneratedPrefix { get; init; } = "__CLX_";
}
