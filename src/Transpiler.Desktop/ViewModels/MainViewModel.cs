using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Serilog.Events;
using Transpiler.Desktop.Common;
using Transpiler.Desktop.Logging;
using Transpiler.Desktop.Services;
using Transpiler.Desktop.Views;

namespace Transpiler.Desktop.ViewModels;

/// <summary>
/// View model for the main window: file selection, transpilation and settings.
/// </summary>
public sealed class MainViewModel : ViewModelBase
{
    private readonly ISettingsStore _settingsStore;
    private readonly WorkspaceProvider _workspaceProvider;
    private readonly FileTranspileService _fileService;
    private readonly IFileDialogService _dialogService;
    private readonly ObservableCollectionSink _consoleSink;
    private readonly LoggingLevelSwitch _levelSwitch;
    private readonly ILogger<MainViewModel> _logger;

    private TranspilerSettings _settings;
    private bool _isBusy;
    private string? _selectedFile;

    public MainViewModel(
        ISettingsStore settingsStore,
        WorkspaceProvider workspaceProvider,
        FileTranspileService fileService,
        IFileDialogService dialogService,
        ObservableCollectionSink consoleSink,
        LoggingLevelSwitch levelSwitch,
        ILogger<MainViewModel> logger)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _workspaceProvider = workspaceProvider ?? throw new ArgumentNullException(nameof(workspaceProvider));
        _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _consoleSink = consoleSink ?? throw new ArgumentNullException(nameof(consoleSink));
        _levelSwitch = levelSwitch ?? throw new ArgumentNullException(nameof(levelSwitch));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _settings = _settingsStore.Load();
        ApplyVerbosity();

        BrowseCommand = new RelayCommand(Browse, () => !IsBusy);
        TranspileCommand = new RelayCommand(async () => await TranspileAsync(), () => Files.Count > 0 && !IsBusy);
        OpenSettingsCommand = new RelayCommand(OpenSettings, () => !IsBusy);
        OpenEditorCommand = new RelayCommand(OpenEditor);
        OpenIdeCommand = new RelayCommand(OpenIde);
        ClearFilesCommand = new RelayCommand(ClearFiles, () => Files.Count > 0 && !IsBusy);
        ClearConsoleCommand = new RelayCommand(_consoleSink.Clear);

        _logger.LogInformation(
            "CLX Transpiler ready — {Pair}, languages folder {LanguagesFolder}.",
            PairLabel,
            _settings.LanguagesFolder);
    }

    /// <summary>Files queued for transpilation, shown in the ListBox on the left.</summary>
    public ObservableCollection<string> Files { get; } = new();

    /// <summary>Console box entries.</summary>
    public ObservableCollection<LogEntry> ConsoleEntries => _consoleSink.Entries;

    public ICommand BrowseCommand { get; }

    public ICommand TranspileCommand { get; }

    public ICommand OpenSettingsCommand { get; }

    public ICommand OpenEditorCommand { get; }

    public ICommand OpenIdeCommand { get; }

    public ICommand ClearFilesCommand { get; }

    public ICommand ClearConsoleCommand { get; }

    public string? SelectedFile
    {
        get => _selectedFile;
        set => SetProperty(ref _selectedFile, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Header text describing the active source and target languages, e.g. "CLX to CL".</summary>
    public string PairLabel => $"{_settings.SourceLanguage} to {_settings.TargetLanguage}";

    private void Browse()
    {
        var sourceLanguage = _settings.SourceLanguage;
        var extension = _settings.SourceExtension;
        var filter = $"{sourceLanguage} Files (*{extension})|*{extension}";

        var picked = _dialogService.PickFiles($"Select {sourceLanguage} files", filter);
        if (picked.Count == 0)
        {
            return;
        }

        var added = 0;
        foreach (var path in picked)
        {
            if (!Files.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                Files.Add(path);
                added++;
            }
        }

        _logger.LogInformation("{Added} file(s) added to the queue ({Total} total).", added, Files.Count);
        RelayCommand.RaiseCanExecuteChanged();
    }

    private async Task TranspileAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var settings = _settings;
            var files = Files.ToList();

            var succeeded = 0;
            var failed = 0;

            await Task.Run(() =>
            {
                // Workspace acquisition (folders, load, problem logging) is Core
                // policy — one recipe for the batch window and the editor alike.
                var workspace = _workspaceProvider.Load(settings);

                foreach (var file in files)
                {
                    var outcome = _fileService.TranspileFile(file, workspace, settings);
                    if (outcome.Success)
                    {
                        succeeded++;
                    }
                    else
                    {
                        failed++;
                    }
                }
            });

            var level = failed == 0 ? LogLevel.Information : LogLevel.Warning;
            _logger.Log(level, "Batch finished: {Succeeded} succeeded, {Failed} failed.", succeeded, failed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during transpilation.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OpenSettings()
    {
        var languages = _workspaceProvider.Load(_settings).LanguageNames;
        var viewModel = new SettingsViewModel(_settings.Clone(), _dialogService, languages);
        var window = new SettingsWindow
        {
            DataContext = viewModel,
            Owner = System.Windows.Application.Current.MainWindow,
        };

        if (window.ShowDialog() != true)
        {
            return;
        }

        var previousSource = _settings.SourceLanguage;
        _settings = viewModel.Settings;
        _settingsStore.Save(_settings);
        ApplyVerbosity();
        OnPropertyChanged(nameof(PairLabel));

        _logger.LogInformation(
            "Settings saved — {Pair}, languages folder {LanguagesFolder}, verbosity {Verbosity}.",
            PairLabel,
            _settings.LanguagesFolder,
            _settings.Verbosity);

        if (!string.Equals(previousSource, _settings.SourceLanguage, StringComparison.OrdinalIgnoreCase) && Files.Count > 0)
        {
            Files.Clear();
            _logger.LogWarning("Source language changed; the file queue was cleared because the source extension differs.");
        }

        RelayCommand.RaiseCanExecuteChanged();
    }

    private void ClearFiles()
    {
        Files.Clear();
        SelectedFile = null;
        _logger.LogInformation("File queue cleared.");
        RelayCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Factory injected by the host so each editor gets fresh services.</summary>
    public Func<Views.EditorWindow>? EditorFactory { get; set; }

    private void OpenEditor()
    {
        if (EditorFactory is null)
        {
            _logger.LogWarning("Editor factory not configured.");
            return;
        }

        try
        {
            var window = EditorFactory();
            window.Owner = System.Windows.Application.Current.MainWindow;
            window.Show();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not open the editor window.");
        }
    }

    /// <summary>Factory injected by the host so each IDE window gets fresh services.</summary>
    public Func<Views.IdeWindow>? IdeFactory { get; set; }

    private void OpenIde()
    {
        if (IdeFactory is null)
        {
            _logger.LogWarning("IDE factory not configured.");
            return;
        }

        try
        {
            var window = IdeFactory();
            window.Owner = System.Windows.Application.Current.MainWindow;
            window.Show();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not open the IDE window.");
        }
    }

    /// <summary>Pushes the configured verbosity onto the shared Serilog level switch.</summary>
    private void ApplyVerbosity() => _levelSwitch.MinimumLevel = MapVerbosity(_settings.Verbosity);

    private static LogEventLevel MapVerbosity(LogLevel level) => level switch
    {
        LogLevel.Trace => LogEventLevel.Verbose,
        LogLevel.Debug => LogEventLevel.Debug,
        LogLevel.Information => LogEventLevel.Information,
        LogLevel.Warning => LogEventLevel.Warning,
        LogLevel.Error => LogEventLevel.Error,
        LogLevel.Critical => LogEventLevel.Fatal,
        _ => LogEventLevel.Information,
    };
}
