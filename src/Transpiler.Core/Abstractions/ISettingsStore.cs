namespace Transpiler.Core.Abstractions;

/// <summary>
/// Persists <see cref="TranspilerSettings"/> between application runs.
/// </summary>
public interface ISettingsStore
{
    /// <summary>Loads the stored settings, or defaults when nothing has been saved yet.</summary>
    TranspilerSettings Load();

    /// <summary>Saves the settings.</summary>
    void Save(TranspilerSettings settings);
}
