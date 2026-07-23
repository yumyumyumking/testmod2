using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Transpiler.Core.Configuration;

/// <summary>
/// User-configurable settings, persisted between runs through
/// <see cref="Abstractions.ISettingsStore"/> (the JSON-file implementation lives in
/// Transpiler.Infrastructure) and edited through the application's Settings dialog.
/// </summary>
public sealed class TranspilerSettings
{
    /// <summary>
    /// The folder containing language files (one JSON per language: recipe, keywords,
    /// capabilities, mappings). A relative value is resolved against the application
    /// base directory, so the default ships alongside the executable.
    /// </summary>
    public string LanguagesFolder { get; set; } = "languages";

    /// <summary>The source language name (registry key, e.g. "CLX"). Any registered language.</summary>
    public string SourceLanguage { get; set; } = "CLX";

    /// <summary>The target language name (registry key, e.g. "CL"). Any registered language.</summary>
    public string TargetLanguage { get; set; } = "CL";

    /// <summary>When true, an existing output file is copied to *.bak before being overwritten.</summary>
    public bool CreateBackup { get; set; } = true;

    /// <summary>When false, transpilation is skipped if the output file already exists.</summary>
    public bool OverwriteExisting { get; set; } = true;

    /// <summary>Minimum severity shown in the console box (mapped onto the Serilog level switch by the host).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public LogLevel Verbosity { get; set; } = LogLevel.Information;

    /// <summary>Source file extension, by convention ".&lt;lowercased source language&gt;" (CLX → .clx).</summary>
    [JsonIgnore]
    public string SourceExtension => "." + SourceLanguage.ToLowerInvariant();

    /// <summary>Output file extension, by convention ".&lt;lowercased target language&gt;" (CL → .cl).</summary>
    [JsonIgnore]
    public string TargetExtension => "." + TargetLanguage.ToLowerInvariant();

    /// <summary>Resolves <see cref="LanguagesFolder"/> to an absolute path.</summary>
    public string ResolveLanguagesFolder(string baseDirectory) => Resolve(LanguagesFolder, baseDirectory);

    private static string Resolve(string folder, string baseDirectory)
    {
        ArgumentNullException.ThrowIfNull(baseDirectory);
        return Path.IsPathRooted(folder)
            ? folder
            : Path.GetFullPath(Path.Combine(baseDirectory, folder));
    }

    /// <summary>Creates an independent copy (used by the Settings dialog for cancel support).</summary>
    public TranspilerSettings Clone() => (TranspilerSettings)MemberwiseClone();
}
