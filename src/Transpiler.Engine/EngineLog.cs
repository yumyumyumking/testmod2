using Microsoft.Extensions.Logging;

namespace Transpiler.Engine;

/// <summary>
/// Source-generated logging delegates (<c>LoggerMessage</c>) for the per-file /
/// per-pass hot path: one <see cref="RunningPass"/> per pass, one
/// <see cref="Transpiling"/>/<see cref="TranspileCompleted"/> per file. These are the
/// calls that run inside the batch loop, so they avoid the boxing/formatting cost of
/// the ILogger extensions (CA1848). One-time workspace logging stays on the plain
/// extensions and is exempted from CA1848 in GlobalSuppressions.cs — it is not on a
/// hot path. (File-writing delegates live with the file writer, in
/// Transpiler.Infrastructure's InfrastructureLog.)
/// </summary>
internal static partial class EngineLog
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "Running pass '{Pass}'.")]
    internal static partial void RunningPass(ILogger logger, string pass);

    [LoggerMessage(Level = LogLevel.Information, Message = "Transpiling {Source} -> {Target} ({File}).")]
    internal static partial void Transpiling(ILogger logger, string source, string target, string file);

    [LoggerMessage(Level = LogLevel.Information, Message = "Done in {Elapsed} ms — {Warnings} warning(s), {Errors} error(s).")]
    internal static partial void TranspileCompleted(ILogger logger, long elapsed, int warnings, int errors);
}
