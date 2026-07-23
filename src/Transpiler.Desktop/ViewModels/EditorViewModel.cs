using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Transpiler.Desktop.Common;
using Transpiler.Desktop.Services;

namespace Transpiler.Desktop.ViewModels;

/// <summary>
/// View model for the live editor: CLX source on the left with debounced
/// syntax/semantic checking, lowered CL preview on the right. Every keystroke
/// restarts a short timer; when it fires, the full pipeline runs off the UI thread
/// and the preview + problems list refresh. The last good preview is kept while
/// the source has errors, so the right pane never flickers to empty.
/// </summary>
public sealed class EditorViewModel : ViewModelBase
{
    private readonly TranspileEngine _engine;
    private readonly WorkspaceProvider _workspaceProvider;
    private readonly IFileDialogService _dialogService;
    private readonly ILogger<EditorViewModel> _logger;
    private readonly DispatcherTimer _debounce;

    private TranspilerWorkspace _workspace;
    private string _sourceText = string.Empty;
    private string _previewText = string.Empty;
    private string _statusText = "Ready.";
    private bool _previewStale;
    private string? _currentFilePath;
    private int _runVersion;
    private string _sourceLanguage = "CLX";
    private string _targetLanguage = "CL";

    public EditorViewModel(
        TranspileEngine engine,
        WorkspaceProvider workspaceProvider,
        IFileDialogService dialogService,
        ILogger<EditorViewModel> logger)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _workspaceProvider = workspaceProvider ?? throw new ArgumentNullException(nameof(workspaceProvider));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _workspace = _workspaceProvider.Load();

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            _ = RefreshAsync();
        };

        RebuildLanguages();

        OpenCommand = new RelayCommand(Open);
        SaveClxCommand = new RelayCommand(SaveClx);
        SaveClCommand = new RelayCommand(SaveCl, () => !string.IsNullOrEmpty(PreviewText));
        ReloadLanguagesCommand = new RelayCommand(ReloadWorkspace);

        SourceText = DefaultTemplate;
    }

    /// <summary>Raised on the UI thread after every completed analysis run.</summary>
    public event EventHandler? DiagnosticsRefreshed;

    public ObservableCollection<DiagnosticItem> Problems { get; } = new();

    /// <summary>Source languages: every registered language (drop a JSON in and it appears).</summary>
    public ObservableCollection<string> SourceLanguages { get; } = new();

    /// <summary>Valid targets for the current source — revalidated whenever the source changes.</summary>
    public ObservableCollection<string> TargetLanguages { get; } = new();

    /// <summary>The language being edited (left pane); syntax checking follows it.</summary>
    public string SourceLanguage
    {
        get => _sourceLanguage;
        set
        {
            if (string.IsNullOrEmpty(value) || !SetProperty(ref _sourceLanguage, value))
            {
                return;
            }

            RebuildTargets(); // the right dropdown is validated against the new source
            OnPropertyChanged(nameof(SourceHeader));
            OnPropertyChanged(nameof(SourceProfile));
            _debounce.Stop();
            _debounce.Start();
        }
    }

    /// <summary>The language the preview is produced in (right pane).</summary>
    public string TargetLanguage
    {
        get => _targetLanguage;
        set
        {
            if (string.IsNullOrEmpty(value) || !SetProperty(ref _targetLanguage, value))
            {
                return;
            }

            OnPropertyChanged(nameof(TargetHeader));
            _debounce.Stop();
            _debounce.Start();
        }
    }

    public string SourceHeader => $"{SourceLanguage} source (editing)";

    public string TargetHeader => $"{TargetLanguage} output (live)";

    /// <summary>The language profile whose keywords drive completion for the source pane.</summary>
    public LanguageProfile SourceProfile => _workspace.Language(SourceLanguage);

    /// <summary>
    /// Valid target languages for a source. Every pair composes through the IR, so the
    /// only rule is target ≠ source (you translate between two languages); tighten here later.
    /// </summary>
    private IEnumerable<string> ValidTargetsFor(string source) =>
        _workspace.LanguageNames.Where(name => !string.Equals(name, source, StringComparison.OrdinalIgnoreCase));

    private void RebuildLanguages()
    {
        var names = _workspace.LanguageNames;

        SourceLanguages.Clear();
        foreach (var name in names)
        {
            SourceLanguages.Add(name);
        }

        if (!names.Contains(_sourceLanguage, StringComparer.OrdinalIgnoreCase))
        {
            _sourceLanguage = names.FirstOrDefault(n => n.Equals("CLX", StringComparison.OrdinalIgnoreCase))
                ?? (names.Count > 0 ? names[0] : "CLX");
            OnPropertyChanged(nameof(SourceLanguage));
        }

        RebuildTargets();
        OnPropertyChanged(nameof(SourceHeader));
        OnPropertyChanged(nameof(SourceProfile));
    }

    private void RebuildTargets()
    {
        var valid = ValidTargetsFor(_sourceLanguage).ToList();

        TargetLanguages.Clear();
        foreach (var name in valid)
        {
            TargetLanguages.Add(name);
        }

        if (!valid.Contains(_targetLanguage, StringComparer.OrdinalIgnoreCase))
        {
            _targetLanguage = valid.FirstOrDefault(n => n.Equals("CL", StringComparison.OrdinalIgnoreCase))
                ?? (valid.Count > 0 ? valid[0] : _sourceLanguage);
            OnPropertyChanged(nameof(TargetLanguage));
            OnPropertyChanged(nameof(TargetHeader));
        }
    }

    public ICommand OpenCommand { get; }

    public ICommand SaveClxCommand { get; }

    public ICommand SaveClCommand { get; }

    public ICommand ReloadLanguagesCommand { get; }

    /// <summary>The CLX text being edited (left pane).</summary>
    public string SourceText
    {
        get => _sourceText;
        set
        {
            if (SetProperty(ref _sourceText, value))
            {
                _debounce.Stop();
                _debounce.Start();
            }
        }
    }

    /// <summary>The lowered CL (right pane); last good output while errors exist.</summary>
    public string PreviewText
    {
        get => _previewText;
        private set => SetProperty(ref _previewText, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>True while the preview shows an older, last-good translation.</summary>
    public bool PreviewStale
    {
        get => _previewStale;
        private set => SetProperty(ref _previewStale, value);
    }

    private async Task RefreshAsync()
    {
        try
        {
            var version = ++_runVersion;
            var source = SourceText;
            var workspace = _workspace;
            var from = SourceLanguage;
            var to = TargetLanguage;

            var result = await Task.Run(() => _engine.Transpile(
                source,
                from,
                to,
                workspace,
                new TranspileOptions { VerifyRoundTrip = false },
                _currentFilePath ?? "editor" + ExtensionFor(from)));

            if (version != _runVersion)
            {
                return; // A newer edit superseded this run.
            }

            Problems.Clear();
            foreach (var diagnostic in result.Diagnostics
                         .OrderByDescending(static d => d.Severity)
                         .ThenBy(static d => d.Position?.Line ?? int.MaxValue))
            {
                Problems.Add(new DiagnosticItem(diagnostic));
            }

            var errors = result.Diagnostics.Count(static d => d.Severity == DiagnosticSeverity.Error);
            var warnings = result.Diagnostics.Count(static d => d.Severity == DiagnosticSeverity.Warning);

            if (result.Success)
            {
                PreviewText = result.OutputText;
                PreviewStale = false;
                StatusText = warnings == 0
                    ? "OK — translated."
                    : $"OK — translated with {warnings} warning(s).";
            }
            else
            {
                PreviewStale = !string.IsNullOrEmpty(PreviewText);
                StatusText = $"{errors} error(s), {warnings} warning(s) — fix the source to refresh the preview.";
            }

            RelayCommand.RaiseCanExecuteChanged();
            DiagnosticsRefreshed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            // Refresh runs fire-and-forget off a timer; an engine or UI failure must
            // surface in the status bar, never as an unhandled-exception storm.
            StatusText = $"Internal error: {ex.GetBaseException().Message}";
            _logger.LogError(ex, "Editor refresh failed.");
        }
    }

    /// <summary>File extension convention (engine policy; see TranspilerWorkspace).</summary>
    private static string ExtensionFor(string languageName) =>
        TranspilerWorkspace.DefaultExtensionFor(languageName);

    /// <summary>
    /// Tab-completion candidates — language knowledge lives in the engine's
    /// <see cref="CompletionProvider"/>; this view model contributes only the
    /// current language and document.
    /// </summary>
    public IReadOnlyList<string> GetCompletions(string prefix) =>
        CompletionProvider.GetCompletions(SourceProfile, SourceText, prefix);

    private void Open()
    {
        var picked = _dialogService.PickFiles("Open CLX file", "CLX Files (*.clx)|*.clx|All Files (*.*)|*.*");
        if (picked.Count == 0)
        {
            return;
        }

        try
        {
            _currentFilePath = picked[0];
            SourceText = File.ReadAllText(picked[0]);
            StatusText = $"Opened {Path.GetFileName(picked[0])}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = $"Could not open file: {ex.Message}";
        }
    }

    private void SaveClx()
    {
        var ext = ExtensionFor(SourceLanguage);
        var path = _dialogService.PickSaveFile("Save source", $"{SourceLanguage} Files (*{ext})|*{ext}",
            Path.GetFileName(_currentFilePath) ?? "untitled" + ext);
        if (path is null)
        {
            return;
        }

        try
        {
            File.WriteAllText(path, SourceText);
            _currentFilePath = path;
            StatusText = $"Saved {Path.GetFileName(path)}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = $"Could not save: {ex.Message}";
        }
    }

    private void SaveCl()
    {
        var ext = ExtensionFor(TargetLanguage);
        var suggested = _currentFilePath is null
            ? "untitled" + ext
            : Path.GetFileNameWithoutExtension(_currentFilePath) + ext;
        var path = _dialogService.PickSaveFile("Save output", $"{TargetLanguage} Files (*{ext})|*{ext}", suggested);
        if (path is null)
        {
            return;
        }

        try
        {
            File.WriteAllText(path, PreviewText);
            StatusText = $"Saved {Path.GetFileName(path)}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = $"Could not save: {ex.Message}";
        }
    }

    private void ReloadWorkspace()
    {
        _workspace = _workspaceProvider.Load();
        RebuildLanguages();
        StatusText = "Languages reloaded.";
        _ = RefreshAsync();
    }

    private const string DefaultTemplate =
@"-- New CLX sequence
SEQUENCE Demo (PM; POINT DemoDb)

EXTERNAL Start : LOGICAL
LOCAL Count : NUMBER

PHASE Main
STEP Init
  Count = 0
  IF Start THEN
    REPEAT 3 TIMES
      Count = Count + 1
    ENDREPEAT
  ENDIF
END Init
";
}
