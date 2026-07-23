namespace Transpiler.Engine;

/// <summary>Per-run options.</summary>
public sealed class TranspileOptions
{
    public static TranspileOptions Default { get; } = new();

    /// <summary>
    /// When lowering, lift the emitted CL back in memory and compare structurally
    /// with the source (SPEC §8.4/§9). Divergence is reported as CLX3002.
    /// </summary>
    public bool VerifyRoundTrip { get; init; } = true;

    public FormattingProfile Formatting { get; init; } = FormattingProfile.Default;

    /// <summary>Tier-3 plugin passes appended after the built-in pipeline (SPEC §7.3).</summary>
    public IReadOnlyList<IAstPass> ExtraPasses { get; init; } = Array.Empty<IAstPass>();
}
