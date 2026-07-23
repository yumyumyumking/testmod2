namespace Transpiler.Engine.Emit;

/// <summary>Formatting knobs for the pretty printers.</summary>
public sealed class FormattingProfile
{
    public static FormattingProfile Default { get; } = new();

    public int IndentSize { get; init; } = 2;

    /// <summary>Labels are printed flush-left; statements at routine indent.</summary>
    public bool LabelsFlushLeft { get; init; } = true;
}
