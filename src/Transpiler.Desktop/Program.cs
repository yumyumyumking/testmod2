using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Transpiler.Desktop.Logging;
using Transpiler.Desktop.Services;
using Transpiler.Desktop.ViewModels;
using Transpiler.Desktop.Views;

namespace Transpiler.Desktop;

/// <summary>
/// Application entry point and composition root. Builds the Serilog logger and the
/// generic host (with <see cref="ConfigureServices"/>) before creating the WPF
/// <see cref="App"/>. Declared as the assembly's StartupObject in the csproj.
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // The level switch lets the Settings dialog raise or lower verbosity at runtime.
        var levelSwitch = new LoggingLevelSwitch(LogEventLevel.Information);

        // Custom Serilog sink that feeds the on-screen console box.
        var consoleSink = new ObservableCollectionSink();

        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClxTranspiler",
            "logs");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(levelSwitch)
            .Enrich.FromLogContext()
            .WriteTo.Debug()
            .WriteTo.Sink(consoleSink)
            .WriteTo.File(
                Path.Combine(logDirectory, "clx-transpiler-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        try
        {
            Log.Information("Starting CLX Transpiler.");

            // UseSerilog() (no argument) adopts the static Log.Logger configured above;
            // its lifetime stays owned by the Log.CloseAndFlush() call in the finally block.
            var host = Host.CreateDefaultBuilder(args)
                .UseSerilog()
                .ConfigureServices((_, services) => ConfigureServices(services, levelSwitch, consoleSink))
                .Build();

            var app = new App(host);
            app.InitializeComponent();
            return app.Run();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "CLX Transpiler terminated unexpectedly.");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    /// <summary>Registers every service in the composition root.</summary>
    private static void ConfigureServices(
        IServiceCollection services,
        LoggingLevelSwitch levelSwitch,
        ObservableCollectionSink consoleSink)
    {
        // Logging infrastructure shared with the Serilog pipeline.
        services.AddSingleton(levelSwitch);
        services.AddSingleton(consoleSink);

        // The engine (application layer) and the infrastructure adapters around it.
        // Registrations wire outer rings onto inner ones; nothing inward knows these types.
        services.AddSingleton<TranspileEngine>();
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<LanguageLoader>();
        services.AddSingleton<WorkspaceLoader>();
        services.AddSingleton<WorkspaceProvider>();
        services.AddSingleton<FileTranspileService>();

        // UI services and view models.
        services.AddSingleton<IFileDialogService, FileDialogService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        // Editor and IDE windows are transient: each Open gets a fresh view model.
        services.AddTransient<EditorViewModel>();
        services.AddTransient<EditorWindow>();
        services.AddTransient<IdeViewModel>();
        services.AddTransient<IdeWindow>();
    }
}
