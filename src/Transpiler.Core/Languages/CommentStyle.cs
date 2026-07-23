namespace Transpiler.Core.Languages;

/// <summary>Comment configuration for a language.</summary>
public sealed class CommentStyle
{
    /// <summary>
    /// Marker that starts a to-end-of-line comment. Defaults to "--" because "!"
    /// introduces database point bindings in declarations.
    /// </summary>
    public string Line { get; init; } = "--";
}
