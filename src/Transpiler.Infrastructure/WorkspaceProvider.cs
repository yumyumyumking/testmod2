using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Transpiler.Infrastructure;

/// <summary>
/// The one place a workspace is acquired from user settings: resolve the configured
/// languages folder, load the registry, and surface every load problem through
/// logging. The batch window and the editor both consume this, so the recipe cannot
/// fork between them. Loading fresh per use is the hot-reload story — edit a
/// language JSON, transpile again, the change is live.
/// </summary>
public sealed class WorkspaceProvider
{
    private readonly ISettingsStore _settingsStore;
    private readonly WorkspaceLoader _loader;
    private readonly ILogger<WorkspaceProvider> _log;

    public WorkspaceProvider(
        ISettingsStore settingsStore,
        WorkspaceLoader loader,
        ILogger<WorkspaceProvider>? log = null)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _log = log ?? NullLogger<WorkspaceProvider>.Instance;
    }

    /// <summary>
    /// Loads the workspace for <paramref name="settings"/> (a caller-held snapshot),
    /// or for the currently stored settings when omitted.
    /// </summary>
    public TranspilerWorkspace Load(TranspilerSettings? settings = null)
    {
        settings ??= _settingsStore.Load();

        var workspace = _loader.Load(settings.ResolveLanguagesFolder(AppContext.BaseDirectory));

        if (workspace.SkippedDisabled > 0)
        {
            _log.LogInformation("{Count} disabled language file(s) skipped.", workspace.SkippedDisabled);
        }

        foreach (var error in workspace.Errors)
        {
            _log.LogWarning("{WorkspaceProblem}", error);
        }

        return workspace;
    }
}
