using Microsoft.Extensions.Logging;

namespace Transpiler.Infrastructure;

/// <summary>
/// Source-generated logging delegates (<c>LoggerMessage</c>) for this layer's hot
/// path: one <see cref="WroteOutput"/> per file written by the batch loop (CA1848).
/// One-time loader/settings logging stays on the plain ILogger extensions and is
/// exempted in GlobalSuppressions.cs — it is not on a hot path.
/// </summary>
internal static partial class InfrastructureLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Wrote {OutputPath}")]
    internal static partial void WroteOutput(ILogger logger, string outputPath);
}
