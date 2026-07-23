namespace Transpiler.Infrastructure.Languages;

/// <summary>Result of scanning the languages folder.</summary>
public sealed class LanguageLoadResult
{
    public LanguageLoadResult(IReadOnlyDictionary<string, LanguageProfile> profiles, IReadOnlyList<string> errors, int skippedDisabled)
    {
        Profiles = profiles;
        Errors = errors;
        SkippedDisabled = skippedDisabled;
    }

    /// <summary>Loaded languages keyed by <see cref="LanguageProfile.Name"/> (case-insensitive).</summary>
    public IReadOnlyDictionary<string, LanguageProfile> Profiles { get; }

    /// <summary>Per-file problems; an invalid language is dropped, never fatal.</summary>
    public IReadOnlyList<string> Errors { get; }

    public int SkippedDisabled { get; }
}
