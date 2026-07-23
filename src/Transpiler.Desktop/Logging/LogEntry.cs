using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using Serilog.Core;
using Serilog.Events;

namespace Transpiler.Desktop.Logging;

/// <summary>
/// One rendered line in the console box.
/// </summary>
public sealed class LogEntry
{
    public LogEntry(DateTimeOffset timestamp, LogEventLevel level, string message)
    {
        Timestamp = timestamp;
        Level = level;
        Message = message;
    }

    public DateTimeOffset Timestamp { get; }

    public LogEventLevel Level { get; }

    public string Message { get; }

    public string Display => $"[{Timestamp.LocalDateTime:HH:mm:ss}] [{Level,-11}] {Message}";
}
