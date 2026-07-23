using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Transpiler.Desktop.Views;

namespace Transpiler.Desktop;

/// <summary>
/// The WPF application object. Instantiation, service configuration and the entry
/// point live in <see cref="Program"/>; this class only owns the host lifecycle and
/// resolves the main window from the container on startup.
/// </summary>
public partial class App : Application
{
    private readonly IHost _host;
    private bool _handlingUnhandledException;
    private int _errorDialogsShown;

    public App(IHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host.Start();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        var mainViewModel = _host.Services.GetRequiredService<ViewModels.MainViewModel>();
        mainViewModel.EditorFactory = () => _host.Services.GetRequiredService<EditorWindow>();
        mainViewModel.IdeFactory = () => _host.Services.GetRequiredService<IdeWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }
        finally
        {
            _host.Dispose();
            Log.CloseAndFlush();
        }

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Always mark handled first, and never re-enter: the handler itself logs, the
        // log feeds the on-screen console, and any exception raised while a modal
        // dialog pumps the dispatcher would otherwise cascade into an infinite storm
        // of error dialogs.
        e.Handled = true;
        if (_handlingUnhandledException)
        {
            return;
        }

        _handlingUnhandledException = true;
        try
        {
            Log.Error(e.Exception, "Unhandled UI exception.");

            if (_errorDialogsShown < 3)
            {
                _errorDialogsShown++;
                var root = e.Exception.GetBaseException();
                MessageBox.Show(
                    $"An unexpected error occurred:\n\n{root.GetType().Name}: {root.Message}\n\n" +
                    @"Details are in the log file (%APPDATA%\ClxTranspiler\logs).",
                    "CLX Transpiler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        catch
        {
            // The error handler must never throw.
        }
        finally
        {
            _handlingUnhandledException = false;
        }
    }
}
