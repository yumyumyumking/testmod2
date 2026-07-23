using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Transpiler.Infrastructure;

/// <summary>
/// File-level orchestration: reads a source file, runs <see cref="TranspileEngine"/>,
/// and writes the result next to the original with the target extension
/// (<c>motion.clx</c> → <c>motion.cl</c>), honouring backup/overwrite settings.
/// Nothing is written when the run produced errors.
/// </summary>
public sealed class FileTranspileService
{
    private readonly TranspileEngine _engine;
    private readonly ILogger<FileTranspileService> _log;

    public FileTranspileService(TranspileEngine engine, ILogger<FileTranspileService>? log = null)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _log = log ?? NullLogger<FileTranspileService>.Instance;
    }

    public FileTranspileOutcome TranspileFile(
        string sourcePath,
        TranspilerWorkspace workspace,
        TranspilerSettings settings,
        TranspileOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(settings);

        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return Fail(sourcePath ?? string.Empty, "No source path provided.");
        }

        if (!File.Exists(sourcePath))
        {
            return Fail(sourcePath, "File not found.");
        }

        var outputPath = Path.ChangeExtension(sourcePath, settings.TargetExtension);
        if (string.Equals(outputPath, sourcePath, StringComparison.OrdinalIgnoreCase))
        {
            return Fail(sourcePath, "Source and target extensions are identical; refusing to overwrite the source file.");
        }

        if (File.Exists(outputPath) && !settings.OverwriteExisting)
        {
            return Fail(sourcePath, $"Output already exists and overwrite is disabled: {outputPath}");
        }

        string sourceText;
        try
        {
            sourceText = File.ReadAllText(sourcePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail(sourcePath, $"Could not read file: {ex.Message}");
        }

        TranspileResult result;
        try
        {
            result = _engine.Transpile(
                sourceText, settings.SourceLanguage, settings.TargetLanguage, workspace, options, Path.GetFileName(sourcePath));
        }
        catch (Exception ex)
        {
            // Per-file isolation: one pathological file (or a misbehaving plugin
            // pass) must fail that file only, never abort the rest of a batch.
            _log.LogError(ex, "Engine failed on {SourcePath}.", sourcePath);
            return Fail(sourcePath, $"Engine error: {ex.GetBaseException().Message}");
        }

        foreach (var diagnostic in result.Diagnostics)
        {
            var level = diagnostic.Severity switch
            {
                DiagnosticSeverity.Error => LogLevel.Error,
                DiagnosticSeverity.Warning => LogLevel.Warning,
                _ => LogLevel.Information,
            };
            _log.Log(level, "{File}: {Diagnostic}", Path.GetFileName(sourcePath), diagnostic);
        }

        if (!result.Success)
        {
            return new FileTranspileOutcome(sourcePath, outputPath: null, result,
                "Transpilation produced errors; output was not written.");
        }

        try
        {
            if (File.Exists(outputPath) && settings.CreateBackup)
            {
                var backupPath = outputPath + ".bak";
                File.Copy(outputPath, backupPath, overwrite: true);
                _log.LogDebug("Backup created: {BackupPath}", backupPath);
            }

            File.WriteAllText(outputPath, result.OutputText);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new FileTranspileOutcome(sourcePath, outputPath: null, result, $"Could not write output: {ex.Message}");
        }

        InfrastructureLog.WroteOutput(_log, outputPath);
        return new FileTranspileOutcome(sourcePath, outputPath, result, failureReason: null);
    }

    private FileTranspileOutcome Fail(string sourcePath, string reason)
    {
        _log.LogError("{SourcePath}: {Reason}", sourcePath, reason);
        return new FileTranspileOutcome(sourcePath, outputPath: null, result: null, reason);
    }
}
