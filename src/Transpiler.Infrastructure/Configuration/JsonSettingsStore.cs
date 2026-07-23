using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Transpiler.Infrastructure.Configuration;

/// <summary>
/// Stores settings as JSON in the user's application-data folder
/// (<c>%APPDATA%\ClxTranspiler\settings.json</c> on Windows). Corrupt or missing
/// files fall back to defaults so the application always starts.
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly string _settingsPath;
    private readonly ILogger<JsonSettingsStore> _log;

    public JsonSettingsStore(ILogger<JsonSettingsStore> log, string? settingsPath = null)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _settingsPath = settingsPath ?? GetDefaultSettingsPath();
    }

    /// <summary>Full path of the backing JSON file.</summary>
    public string SettingsPath => _settingsPath;

    /// <inheritdoc />
    public TranspilerSettings Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                var settings = JsonSerializer.Deserialize<TranspilerSettings>(json, SerializerOptions);
                if (settings is not null)
                {
                    _log.LogDebug("Settings loaded from {Path}.", _settingsPath);
                    return settings;
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _log.LogWarning("Could not read settings ({Error}); using defaults.", ex.Message);
        }

        return new TranspilerSettings();
    }

    /// <inheritdoc />
    public void Save(TranspilerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(settings, SerializerOptions);
            File.WriteAllText(_settingsPath, json);
            _log.LogDebug("Settings saved to {Path}.", _settingsPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogError("Could not save settings: {Error}", ex.Message);
        }
    }

    private static string GetDefaultSettingsPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "ClxTranspiler", "settings.json");
    }
}
