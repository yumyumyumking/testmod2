using Microsoft.Extensions.Logging;

namespace Transpiler.Infrastructure;

/// <summary>Loads the workspace from the configured languages folder.</summary>
public sealed class WorkspaceLoader
{
    private readonly LanguageLoader _languageLoader;
    private readonly ILogger<WorkspaceLoader> _log;

    public WorkspaceLoader(LanguageLoader languageLoader, ILogger<WorkspaceLoader> log)
    {
        _languageLoader = languageLoader ?? throw new ArgumentNullException(nameof(languageLoader));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <param name="overwriteDuplicates">
    /// When two language files declare the same language name, the later file wins by
    /// default; pass false to keep the first and report the clash instead.
    /// </param>
    public TranspilerWorkspace Load(string languagesFolder, bool overwriteDuplicates = true)
    {
        var languages = _languageLoader.Load(languagesFolder, overwriteDuplicates);

        var workspace = new TranspilerWorkspace(
            languages.Profiles,
            languages.Errors,
            languages.SkippedDisabled);

        _log.LogInformation(
            "Workspace loaded: languages [{Languages}], {Skipped} disabled file(s) skipped, {ErrorCount} problem(s).",
            string.Join(", ", workspace.LanguageNames),
            workspace.SkippedDisabled,
            languages.Errors.Count);

        return workspace;
    }
}
