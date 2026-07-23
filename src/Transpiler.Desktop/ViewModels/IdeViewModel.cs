using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Transpiler.Desktop.Common;
using Transpiler.Desktop.Services;

namespace Transpiler.Desktop.ViewModels;

/// <summary>
/// View model for the file-explorer IDE. Left: a lazily-loaded folder tree. Centre:
/// the CL/CLX buffer being edited (the view draws line numbers and VS Code-style
/// indentation guides over it). Bottom: a live "console" of diagnostics — every edit
/// restarts a short timer that re-runs the pipeline off the UI thread (the same
/// feedback the live editor gives) and refreshes <see cref="Problems"/>. The buffer is
/// analysed as whatever language its file extension maps to (".cl" → CL), so editing a
/// CL file surfaces CL syntax/semantic problems as you type.
/// </summary>
public sealed class IdeViewModel : ViewModelBase
{
    private readonly TranspileEngine _engine;
    private readonly WorkspaceProvider _workspaceProvider;
    private readonly IFileDialogService _dialogService;
    private readonly ILogger<IdeViewModel> _logger;
    private readonly DispatcherTimer _debounce;

    private TranspilerWorkspace _workspace;
    private IReadOnlyDictionary<string, string> _extensionToLanguage =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _fileExtensions = new(StringComparer.OrdinalIgnoreCase);

    private string _sourceText = string.Empty;
    private string _statusText = "Open a folder or file to begin.";
    private string _problemSummary = "No file open.";
    private string? _currentFilePath;
    private string _language = "CL";
    private bool _isDirty;
    private bool _loadingBuffer;
    private int _runVersion;

    public IdeViewModel(
        TranspileEngine engine,
        WorkspaceProvider workspaceProvider,
        IFileDialogService dialogService,
        ILogger<IdeViewModel> logger)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _workspaceProvider = workspaceProvider ?? throw new ArgumentNullException(nameof(workspaceProvider));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _workspace = _workspaceProvider.Load();
        RebuildLanguageMaps();

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            _ = RefreshAsync();
        };

        OpenFolderCommand = new RelayCommand(OpenFolder);
        OpenFileCommand = new RelayCommand(PromptOpenFile);
        SaveCommand = new RelayCommand(Save, () => _currentFilePath is not null || IsDirty);
        SaveAsCommand = new RelayCommand(SaveAs, () => !string.IsNullOrEmpty(SourceText));
        ReloadCommand = new RelayCommand(Reload);
    }

    /// <summary>Root nodes of the file explorer (usually the one opened folder).</summary>
    public ObservableCollection<FileNode> Roots { get; } = new();

    /// <summary>Live diagnostics for the open buffer — the bottom console.</summary>
    public ObservableCollection<DiagnosticItem> Problems { get; } = new();

    public ICommand OpenFolderCommand { get; }

    public ICommand OpenFileCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand SaveAsCommand { get; }

    public ICommand ReloadCommand { get; }

    /// <summary>The text being edited (centre pane).</summary>
    public string SourceText
    {
        get => _sourceText;
        set
        {
            if (SetProperty(ref _sourceText, value))
            {
                if (!_loadingBuffer)
                {
                    IsDirty = true;
                }

                _debounce.Stop();
                _debounce.Start();
            }
        }
    }

    /// <summary>Left of the status bar: the last action or error.</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>Right of the console header: "No problems." / "2 errors · 1 warning".</summary>
    public string ProblemSummary
    {
        get => _problemSummary;
        private set => SetProperty(ref _problemSummary, value);
    }

    /// <summary>True when the buffer has edits not yet written to disk.</summary>
    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (SetProperty(ref _isDirty, value))
            {
                OnPropertyChanged(nameof(FileLabel));
                OnPropertyChanged(nameof(WindowTitle));
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>The language the buffer is analysed as (from the open file's extension).</summary>
    public string Language
    {
        get => _language;
        private set
        {
            if (SetProperty(ref _language, value))
            {
                OnPropertyChanged(nameof(WindowTitle));
            }
        }
    }

    /// <summary>File name with a dirty marker, e.g. "motion.cl •".</summary>
    public string FileLabel =>
        _currentFilePath is null
            ? "untitled"
            : Path.GetFileName(_currentFilePath) + (IsDirty ? " •" : string.Empty);

    public string WindowTitle => $"{Language} IDE — {FileLabel}";

    // ------------------------------------------------------------------- explorer

    private void OpenFolder()
    {
        var folder = _dialogService.PickFolder("Open folder", CurrentFolder());
        if (folder is not null)
        {
            LoadRoot(folder);
        }
    }

    private string? CurrentFolder() =>
        _currentFilePath is not null ? Path.GetDirectoryName(_currentFilePath) : null;

    private void LoadRoot(string folder)
    {
        var name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(name))
        {
            name = folder;
        }

        Roots.Clear();
        Roots.Add(new FileNode(name, folder, isDirectory: true, EnumerateChildren) { IsExpanded = true });
        StatusText = $"Opened folder {folder}.";
    }

    /// <summary>Directory-first, name-sorted children; files filtered to language extensions.</summary>
    private IEnumerable<FileNode> EnumerateChildren(string directory)
    {
        var nodes = new List<FileNode>();
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(directory)
                         .OrderBy(static d => d, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(dir);
                if (!string.IsNullOrEmpty(name) && !name.StartsWith('.'))
                {
                    nodes.Add(new FileNode(name, dir, isDirectory: true, EnumerateChildren));
                }
            }

            foreach (var file in Directory.EnumerateFiles(directory)
                         .OrderBy(static f => f, StringComparer.OrdinalIgnoreCase))
            {
                if (_fileExtensions.Contains(Path.GetExtension(file)))
                {
                    nodes.Add(new FileNode(Path.GetFileName(file), file, isDirectory: false, EnumerateChildren));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not read folder {Directory}.", directory);
        }

        return nodes;
    }

    // ------------------------------------------------------------------- open/save

    /// <summary>Loads a file into the buffer (called by the tree and the Open dialog).</summary>
    public void OpenFile(string path)
    {
        try
        {
            var text = File.ReadAllText(path);
            _currentFilePath = path;
            Language = DetectLanguage(path);
            LoadBuffer(text);
            IsDirty = false;
            StatusText = $"Opened {Path.GetFileName(path)}.";
            OnPropertyChanged(nameof(FileLabel));
            OnPropertyChanged(nameof(WindowTitle));
            RelayCommand.RaiseCanExecuteChanged();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = $"Could not open {Path.GetFileName(path)}: {ex.Message}";
        }
    }

    private void PromptOpenFile()
    {
        var picked = _dialogService.PickFiles("Open file", BuildOpenFilter());
        if (picked.Count > 0)
        {
            OpenFile(picked[0]);
        }
    }

    private void Save()
    {
        if (_currentFilePath is null)
        {
            SaveAs();
            return;
        }

        WriteFile(_currentFilePath);
    }

    private void SaveAs()
    {
        var ext = TranspilerWorkspace.DefaultExtensionFor(Language);
        var suggested = _currentFilePath is null ? "untitled" + ext : Path.GetFileName(_currentFilePath);
        var path = _dialogService.PickSaveFile("Save as", BuildSaveFilter(ext), suggested);
        if (path is null)
        {
            return;
        }

        var isNew = !string.Equals(path, _currentFilePath, StringComparison.OrdinalIgnoreCase);
        _currentFilePath = path;
        Language = DetectLanguage(path);
        OnPropertyChanged(nameof(FileLabel));
        OnPropertyChanged(nameof(WindowTitle));

        if (WriteFile(path) && isNew)
        {
            RefreshRoots();
        }
    }

    private bool WriteFile(string path)
    {
        try
        {
            File.WriteAllText(path, SourceText);
            IsDirty = false;
            StatusText = $"Saved {Path.GetFileName(path)}.";
            OnPropertyChanged(nameof(FileLabel));
            OnPropertyChanged(nameof(WindowTitle));
            RelayCommand.RaiseCanExecuteChanged();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = $"Could not save: {ex.Message}";
            return false;
        }
    }

    private void Reload()
    {
        _workspace = _workspaceProvider.Load();
        RebuildLanguageMaps();
        RefreshRoots();
        StatusText = "Languages reloaded.";
        _debounce.Stop();
        _debounce.Start();
    }

    // ------------------------------------------------------------------- analysis

    private void LoadBuffer(string text)
    {
        _loadingBuffer = true;
        try
        {
            SourceText = text;
        }
        finally
        {
            _loadingBuffer = false;
        }
    }

    private async Task RefreshAsync()
    {
        var version = ++_runVersion;
        var source = SourceText;

        if (string.IsNullOrWhiteSpace(source))
        {
            Problems.Clear();
            ProblemSummary = "No problems.";
            if (_currentFilePath is null)
            {
                StatusText = "Ready — open a file to begin.";
            }

            return;
        }

        try
        {
            var workspace = _workspace;
            var language = Language;
            var fileName = _currentFilePath is null
                ? "ide" + TranspilerWorkspace.DefaultExtensionFor(language)
                : Path.GetFileName(_currentFilePath);

            // Analysing a language against itself runs the full front end (parse + bind +
            // normalize) and re-emits, so every syntax/semantic diagnostic surfaces
            // without needing a second target language. Round-trip verification is off:
            // this is interactive feedback, not a batch translation.
            var result = await Task.Run(() => _engine.Transpile(
                source,
                language,
                language,
                workspace,
                new TranspileOptions { VerifyRoundTrip = false },
                fileName));

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
            ProblemSummary = Summarize(errors, warnings);
        }
        catch (Exception ex)
        {
            // The refresh is fire-and-forget off a timer; a failure must land in the
            // status bar, never as an unhandled-exception storm.
            ProblemSummary = "Analysis error.";
            StatusText = $"Internal error: {ex.GetBaseException().Message}";
            _logger.LogError(ex, "IDE analysis failed.");
        }
    }

    private static string Summarize(int errors, int warnings)
    {
        if (errors == 0 && warnings == 0)
        {
            return "No problems.";
        }

        var parts = new List<string>(2);
        if (errors > 0)
        {
            parts.Add(errors == 1 ? "1 error" : $"{errors} errors");
        }

        if (warnings > 0)
        {
            parts.Add(warnings == 1 ? "1 warning" : $"{warnings} warnings");
        }

        return string.Join(" · ", parts);
    }

    // -------------------------------------------------------------------- helpers

    private void RebuildLanguageMaps()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in _workspace.LanguageNames)
        {
            map[TranspilerWorkspace.DefaultExtensionFor(name)] = name;
        }

        _extensionToLanguage = map;
        _fileExtensions = new HashSet<string>(map.Keys, StringComparer.OrdinalIgnoreCase);
    }

    private string DetectLanguage(string path)
    {
        var ext = Path.GetExtension(path);
        if (!string.IsNullOrEmpty(ext) && _extensionToLanguage.TryGetValue(ext, out var name))
        {
            return name;
        }

        var names = _workspace.LanguageNames;
        if (names.Contains("CL", StringComparer.OrdinalIgnoreCase))
        {
            return "CL";
        }

        return names.Count > 0 ? names[0] : "CL";
    }

    private void RefreshRoots()
    {
        if (Roots.Count > 0)
        {
            LoadRoot(Roots[0].FullPath);
        }
    }

    private string BuildOpenFilter()
    {
        var exts = _fileExtensions.OrderBy(static e => e, StringComparer.OrdinalIgnoreCase).ToList();
        if (exts.Count == 0)
        {
            return "All files (*.*)|*.*";
        }

        var patterns = string.Join(";", exts.Select(static e => "*" + e));
        return $"Language files ({patterns})|{patterns}|All files (*.*)|*.*";
    }

    private static string BuildSaveFilter(string ext) =>
        $"{ext.TrimStart('.').ToUpperInvariant()} file (*{ext})|*{ext}|All files (*.*)|*.*";
}
