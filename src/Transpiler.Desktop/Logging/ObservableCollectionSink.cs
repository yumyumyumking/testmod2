using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using Serilog.Core;
using Serilog.Events;

namespace Transpiler.Desktop.Logging;

/// <summary>
/// A Serilog sink that surfaces log events in the WPF console box. Events are
/// marshalled to the UI thread, so the engine may log from background threads.
/// The same instance is registered in the container and wired into the Serilog
/// pipeline in <see cref="Program"/>.
/// </summary>
public sealed class ObservableCollectionSink : ILogEventSink
{
    private const int MaxEntries = 2000;

    /// <summary>Bound by the console ListBox in the main window.</summary>
    public ObservableCollection<LogEntry> Entries { get; } = new();

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        if (logEvent is null)
        {
            return;
        }

        try
        {
            var message = logEvent.RenderMessage(CultureInfo.CurrentCulture);
            var entry = new LogEntry(logEvent.Timestamp, logEvent.Level, message);
            Dispatch(() => Append(entry));
        }
        catch
        {
            // A UI logging failure must never propagate into the logging pipeline.
        }
    }

    /// <summary>Removes all console entries.</summary>
    public void Clear() => Dispatch(() => Entries.Clear());

    private void Append(LogEntry entry)
    {
        Entries.Add(entry);

        // Keep the console bounded so long batch runs do not grow memory forever.
        while (Entries.Count > MaxEntries)
        {
            Entries.RemoveAt(0);
        }
    }

    private static void Dispatch(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            _ = dispatcher.InvokeAsync(action);
        }
    }
}
